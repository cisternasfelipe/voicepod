using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.App.ViewModels;

/// <summary>
/// First-run assistant: downloads the Parakeet model, or points the app at a copy the
/// user already has on disk.
/// </summary>
public sealed partial class ModelSetupViewModel : ObservableObject
{
    private readonly IModelDownloader _downloader;
    private readonly ITranscriptionService _transcription;
    private readonly ISettingsService _settings;
    private readonly ILogger<ModelSetupViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private string _targetDirectory = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isCompleted;

    public ModelSetupViewModel(
        IModelDownloader downloader,
        ITranscriptionService transcription,
        ISettingsService settings,
        ILogger<ModelSetupViewModel> logger)
    {
        _downloader = downloader;
        _transcription = transcription;
        _settings = settings;
        _logger = logger;

        TargetDirectory = ResolveDirectory();
        StatusText = Strings.Format(
            "SetupSizeFormat",
            (ModelDownloadDefaults.TotalBytes / 1024d / 1024d / 1024d).ToString("0.00"));
    }

    public string SourceUrl => _settings.Current.Stt.ModelBaseUrl;

    /// <summary>Raised when the model is ready and the window can close.</summary>
    public event EventHandler? Finished;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        ErrorText = null;
        IsDownloading = true;
        _cancellation = new CancellationTokenSource();

        var progress = new Progress<ModelDownloadProgress>(report =>
        {
            Progress = report.Fraction * 100;
            ProgressText = Strings.Format(
                "SetupProgressFormat",
                report.CurrentFile,
                report.FileIndex,
                report.FileCount,
                (report.BytesReceived / 1024d / 1024d).ToString("0"),
                (report.TotalBytes / 1024d / 1024d).ToString("0"));
        });

        try
        {
            await _downloader.DownloadAsync(
                TargetDirectory,
                _settings.Current.Stt.ModelBaseUrl,
                _settings.Current.Stt.VerifyHashes,
                progress,
                _cancellation.Token).ConfigureAwait(true);

            await FinishAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = Strings.SetupCancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download failed");
            ErrorText = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task UseLocalFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.SetupPickFolderTitle,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folder = dialog.FolderName;

        if (!Stt.SherpaTranscriptionService.ModelFilesExist(folder, out var missing))
        {
            ErrorText = Strings.Format("SetupIncompleteFolderFormat", missing);
            return;
        }

        _settings.Current.Stt.ModelDirectory = folder;
        await _settings.SaveAsync().ConfigureAwait(true);
        TargetDirectory = folder;
        await FinishAsync().ConfigureAwait(true);
    }

    private async Task FinishAsync()
    {
        StatusText = Strings.SetupLoadingModel;
        await _transcription.ReloadAsync().ConfigureAwait(true);

        IsCompleted = true;
        StatusText = _transcription.State.Status == SttModelStatus.Ready
            ? Strings.SetupModelReady
            : _transcription.State.Message ?? Strings.SetupModelLoadFailed;

        Finished?.Invoke(this, EventArgs.Empty);
    }

    private string ResolveDirectory()
    {
        var configured = _settings.Current.Stt.ModelDirectory;
        return string.IsNullOrWhiteSpace(configured)
            ? AppPaths.DefaultModelDirectory
            : configured;
    }
}
