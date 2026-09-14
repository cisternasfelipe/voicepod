using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Llm;

namespace VoiceFlow.App.ViewModels;

/// <summary>One history row as shown in the list.</summary>
public sealed partial class HistoryEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _processedText;

    [ObservableProperty]
    private string _profileName;

    public HistoryEntryViewModel(HistoryEntry entry)
    {
        Entry = entry;
        _processedText = entry.ProcessedText;
        _profileName = entry.ProfileName;
    }

    public HistoryEntry Entry { get; }

    public long Id => Entry.Id;

    public string LocalTimestamp => Entry.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public string RawTranscript => Entry.RawTranscript;

    public string Summary =>
        $"{Entry.DurationMs / 1000.0:0.0} s · STT {Entry.SttLatencyMs} ms" +
        (Entry.LlmLatencyMs > 0 ? $" · LLM {Entry.LlmLatencyMs} ms" : string.Empty) +
        $" · {Entry.ModelUsed}";

    public bool HasProblem => !Entry.PasteSucceeded || Entry.LlmError is not null;

    public string ProblemText => Entry.LlmError is not null
        ? "LLM: " + Entry.LlmError
        : Entry.PasteSucceeded ? string.Empty : Strings.HistoryPasteProblem;

    public void Refresh()
    {
        ProcessedText = Entry.ProcessedText;
        ProfileName = Entry.ProfileName;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(ProblemText));
    }
}

/// <summary>Search, filtering and per-entry actions over the dictation history.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryRepository _history;
    private readonly IClipboardService _clipboard;
    private readonly ILlmClient _llm;
    private readonly PromptProfileService _profiles;
    private readonly ILogger<HistoryViewModel> _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _profileFilter;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public HistoryViewModel(
        IHistoryRepository history,
        IClipboardService clipboard,
        ILlmClient llm,
        PromptProfileService profiles,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _clipboard = clipboard;
        _llm = llm;
        _profiles = profiles;
        _logger = logger;
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public ObservableCollection<string> ProfileFilters { get; } = [];

    public IReadOnlyList<PromptProfile> Profiles => _profiles.Profiles;

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    partial void OnProfileFilterChanged(string? value) => _ = RefreshAsync();

    partial void OnFromDateChanged(DateTime? value) => _ = RefreshAsync();

    partial void OnToDateChanged(DateTime? value) => _ = RefreshAsync();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;

            var query = new HistoryQuery(
                SearchText,
                string.IsNullOrWhiteSpace(ProfileFilter) || ProfileFilter == AllProfilesLabel ? null : ProfileFilter,
                FromDate?.ToUniversalTime(),
                ToDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime(),
                Limit: 1000);

            var entries = await _history.QueryAsync(query).ConfigureAwait(true);

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(new HistoryEntryViewModel(entry));
            }

            await RefreshProfileFiltersAsync().ConfigureAwait(true);
            StatusText = Entries.Count == 0
                ? Strings.HistoryEmpty
                : Strings.Format("HistoryCountFormat", Entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the history");
            StatusText = Strings.Format("HistoryReadErrorFormat", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static string AllProfilesLabel => Strings.HistoryAllProfiles;

    private async Task RefreshProfileFiltersAsync()
    {
        var names = await _history.GetProfileNamesAsync().ConfigureAwait(true);
        var current = ProfileFilter;

        ProfileFilters.Clear();
        ProfileFilters.Add(AllProfilesLabel);

        foreach (var name in names)
        {
            ProfileFilters.Add(name);
        }

        if (current is not null && ProfileFilters.Contains(current))
        {
            ProfileFilter = current;
        }
    }

    [RelayCommand]
    private async Task CopyProcessedAsync(HistoryEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        await _clipboard.SetTextAsync(entry.Entry.ProcessedText).ConfigureAwait(true);
        StatusText = Strings.HistoryProcessedCopied;
    }

    [RelayCommand]
    private async Task CopyRawAsync(HistoryEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        await _clipboard.SetTextAsync(entry.Entry.RawTranscript).ConfigureAwait(true);
        StatusText = Strings.HistoryRawCopied;
    }

    [RelayCommand]
    private async Task DeleteAsync(HistoryEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        await _history.DeleteAsync(entry.Id).ConfigureAwait(true);
        Entries.Remove(entry);
        StatusText = Strings.HistoryEntryDeleted;
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        var answer = MessageBox.Show(
            Strings.HistoryConfirmClear,
            "VoiceFlow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _history.ClearAsync().ConfigureAwait(true);
        Entries.Clear();
        StatusText = Strings.HistoryCleared;
    }

    /// <summary>Runs the stored raw transcript through another profile without recording again.</summary>
    [RelayCommand]
    public async Task ReprocessAsync(ReprocessRequest? request)
    {
        if (request?.Entry is null || request.Profile is null)
        {
            return;
        }

        var entry = request.Entry;
        var profile = request.Profile;

        try
        {
            IsBusy = true;
            StatusText = Strings.Format("HistoryReprocessingFormat", profile.Name);

            if (!profile.UsesLlm)
            {
                entry.Entry.ProcessedText = entry.Entry.RawTranscript;
                entry.Entry.ProfileName = profile.Name;
                entry.Entry.ModelUsed = VoiceFlow.Core.Pipeline.DictationPipeline.NoLlmModelName;
                entry.Entry.LlmError = null;
                entry.Entry.LlmLatencyMs = 0;
            }
            else
            {
                var result = await _llm.ProcessAsync(profile, entry.Entry.RawTranscript).ConfigureAwait(true);

                if (result.Failed)
                {
                    StatusText = Strings.Format("HistoryLlmFailedFormat", result.Error);
                    entry.Entry.LlmError = result.Error;
                    await _history.UpdateAsync(entry.Entry).ConfigureAwait(true);
                    entry.Refresh();
                    return;
                }

                entry.Entry.ProcessedText = result.Text;
                entry.Entry.ProfileName = profile.Name;
                entry.Entry.ModelUsed = result.ModelUsed;
                entry.Entry.LlmLatencyMs = result.LatencyMs;
                entry.Entry.LlmError = null;
            }

            await _history.UpdateAsync(entry.Entry).ConfigureAwait(true);
            entry.Refresh();
            await _clipboard.SetTextAsync(entry.Entry.ProcessedText).ConfigureAwait(true);
            StatusText = Strings.HistoryReprocessedCopied;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reprocessing failed");
            StatusText = Strings.Format("HistoryReprocessErrorFormat", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        ProfileFilter = AllProfilesLabel;
        FromDate = null;
        ToDate = null;
    }
}

/// <summary>Pairs a history row with the profile chosen in its reprocess menu.</summary>
public sealed record ReprocessRequest(HistoryEntryViewModel Entry, PromptProfile Profile);
