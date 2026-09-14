using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.App.ViewModels;

/// <summary>
/// Browses the model catalogue published by the configured endpoint. OpenRouter lists
/// hundreds of models with prices, so the list is filterable and can hide paid ones.
/// </summary>
public sealed partial class ModelPickerViewModel : ObservableObject
{
    private readonly ILlmClient _llm;
    private readonly ILogger<ModelPickerViewModel> _logger;
    private readonly List<LlmModelInfo> _all = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _onlyFree;

    [ObservableProperty]
    private LlmModelInfo? _selectedModel;

    [ObservableProperty]
    private string _statusText = Strings.ModelPickerLoading;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    public ModelPickerViewModel(ILlmClient llm, ILogger<ModelPickerViewModel> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public ObservableCollection<LlmModelInfo> Models { get; } = [];

    /// <summary>Raised when the user picked a model and the window can close.</summary>
    public event EventHandler<string>? ModelChosen;

    /// <summary>Model id selected before opening the picker, preselected in the list.</summary>
    public string? InitialModelId { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnOnlyFreeChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorText = null;
        StatusText = Strings.ModelPickerLoading;

        try
        {
            var models = await _llm.GetModelsAsync().ConfigureAwait(true);

            _all.Clear();
            _all.AddRange(models);
            ApplyFilter();

            if (_all.Count == 0)
            {
                StatusText = Strings.ModelPickerEmpty;
            }

            if (!string.IsNullOrWhiteSpace(InitialModelId))
            {
                SelectedModel = Models.FirstOrDefault(m =>
                    string.Equals(m.Id, InitialModelId, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the model catalogue");
            ErrorText = Strings.Format("ModelPickerErrorFormat", ex.Message);
            StatusText = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Choose()
    {
        if (SelectedModel is not null)
        {
            ModelChosen?.Invoke(this, SelectedModel.Id);
        }
    }

    private void ApplyFilter()
    {
        var needle = SearchText.Trim();
        var previous = SelectedModel?.Id;

        IEnumerable<LlmModelInfo> filtered = _all;

        if (OnlyFree)
        {
            filtered = filtered.Where(m => m.IsFree);
        }

        if (needle.Length > 0)
        {
            filtered = filtered.Where(m =>
                m.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (m.Name?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Models.Clear();
        foreach (var model in filtered)
        {
            Models.Add(model);
        }

        if (previous is not null)
        {
            SelectedModel = Models.FirstOrDefault(m =>
                string.Equals(m.Id, previous, StringComparison.OrdinalIgnoreCase));
        }

        if (_all.Count > 0)
        {
            StatusText = Strings.Format("ModelPickerCountFormat", Models.Count, _all.Count);
        }
    }
}
