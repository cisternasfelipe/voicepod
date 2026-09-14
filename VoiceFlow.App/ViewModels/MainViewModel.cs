using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceFlow.App.Resources;
using VoiceFlow.App.Services;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Core.Pipeline;

namespace VoiceFlow.App.ViewModels;

/// <summary>View model behind the main window: live pipeline state, level meter and the last result.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DictationPipeline _pipeline;
    private readonly ISettingsService _settings;
    private readonly ITranscriptionService _transcription;

    [ObservableProperty]
    private string _statusText = Strings.StateIdle;

    [ObservableProperty]
    private string _stateDetail = string.Empty;

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _hotkeyText = string.Empty;

    [ObservableProperty]
    private string? _hotkeyError;

    [ObservableProperty]
    private string _lastResult = string.Empty;

    [ObservableProperty]
    private string _lastRecordingInfo = string.Empty;

    [ObservableProperty]
    private string _modelStatusText = Strings.ModelStatusChecking;

    [ObservableProperty]
    private bool _isModelReady;

    [ObservableProperty]
    private bool _isCopied;

    [ObservableProperty]
    private string _engineBadge = "Nube OpenRouter";

    [ObservableProperty]
    private string _profileBadge = string.Empty;

    public bool HasLastResult => !string.IsNullOrWhiteSpace(LastResult);

    partial void OnLastResultChanged(string value) => OnPropertyChanged(nameof(HasLastResult));

    public MainViewModel(
        DictationPipeline pipeline,
        ISettingsService settings,
        ITranscriptionService transcription)
    {
        _pipeline = pipeline;
        _settings = settings;
        _transcription = transcription;

        _pipeline.StateChanged += OnStateChanged;
        _pipeline.LevelChanged += OnLevelChanged;
        _pipeline.Completed += OnCompleted;
        _transcription.StateChanged += OnModelStateChanged;
        _settings.SettingsChanged += (_, _) => Post(RefreshSettingsInfo);

        RefreshSettingsInfo();
        ApplyModelState(_transcription.State);
    }

    public string ModeText
    {
        get
        {
            var hk = _settings.Current.Hotkey;
            if (hk.HoldHotkey is not null && hk.ToggleHotkey is not null)
            {
                return "Mantener + Alternar";
            }
            if (hk.HoldHotkey is not null)
            {
                return Strings.ModeHold;
            }
            if (hk.ToggleHotkey is not null)
            {
                return Strings.ModeToggle;
            }
            return hk.Mode == HotkeyMode.PushToTalk ? Strings.ModeHold : Strings.ModeToggle;
        }
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? HistoryRequested;

    [RelayCommand]
    private void ToggleDictation() => _pipeline.Toggle();

    [RelayCommand]
    private void CancelDictation() => _pipeline.CancelRecording();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenHistory() => HistoryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task CopyLastResultAsync()
    {
        if (string.IsNullOrWhiteSpace(LastResult))
        {
            return;
        }

        try
        {
            Clipboard.SetText(LastResult);
            IsCopied = true;
            await Task.Delay(2000).ConfigureAwait(true);
            IsCopied = false;
        }
        catch
        {
            // Ignore temporary clipboard locking by other apps
        }
    }

    public void SetHotkeyError(string? error) => Post(() => HotkeyError = error);

    private void RefreshSettingsInfo()
    {
        RefreshHotkeyText();
        var current = _settings.Current;
        ProfileBadge = current.GetActiveProfile().Name;
        EngineBadge = current.Stt.Provider == SttProvider.OpenRouterCloud
            ? "Nube (" + (CloudSttCatalog.Models.FirstOrDefault(m => m.Id == current.Stt.CloudModel)?.DisplayName ?? "MAI-Transcribe 2") + ")"
            : $"Local ({current.Stt.Provider})";
    }

    private void RefreshHotkeyText()
    {
        var hk = _settings.Current.Hotkey;
        if (hk.HoldHotkey is not null && hk.ToggleHotkey is not null)
        {
            HotkeyText = $"{HotkeyFormatter.Describe(hk.HoldHotkey)} · {HotkeyFormatter.Describe(hk.ToggleHotkey)}";
        }
        else if (hk.HoldHotkey is not null)
        {
            HotkeyText = HotkeyFormatter.Describe(hk.HoldHotkey);
        }
        else if (hk.ToggleHotkey is not null)
        {
            HotkeyText = HotkeyFormatter.Describe(hk.ToggleHotkey);
        }
        else
        {
            HotkeyText = HotkeyFormatter.Describe(hk.ToDefinition());
        }
        OnPropertyChanged(nameof(ModeText));
    }

    private void OnStateChanged(object? sender, DictationStateChangedEventArgs e) => Post(() =>
    {
        IsRecording = e.State == DictationState.Recording;
        StatusText = Describe(e.State);
        StateDetail = DescribeMessage(e);

        if (!IsRecording)
        {
            AudioLevel = 0;
        }
    });

    private void OnLevelChanged(object? sender, AudioLevelEventArgs e) => Post(() => AudioLevel = e.Peak);

    private void OnCompleted(object? sender, DictationCompletedEventArgs e) => Post(() =>
    {
        var seconds = e.DurationMs / 1000.0;
        LastResult = string.IsNullOrWhiteSpace(e.ProcessedText) ? e.RawTranscript : e.ProcessedText;

        var info = $"{seconds:0.0} s · STT {e.SttLatencyMs} ms · {e.ProfileName}";

        if (e.LlmLatencyMs > 0)
        {
            info += $" · LLM {e.LlmLatencyMs} ms";
        }

        if (e.LlmError is not null)
        {
            info += " · " + Strings.MessageLlmFallback + " " + e.LlmError;
        }

        LastRecordingInfo = info;
    });

    private void OnModelStateChanged(object? sender, SttModelState state) => Post(() => ApplyModelState(state));

    private void ApplyModelState(SttModelState state)
    {
        IsModelReady = state.Status == SttModelStatus.Ready;

        ModelStatusText = state.Status switch
        {
            SttModelStatus.Ready => Strings.ModelStatusReady,
            SttModelStatus.Loading => Strings.ModelStatusLoading,
            SttModelStatus.Downloading => Strings.ModelStatusDownloading,
            SttModelStatus.NotDownloaded => Strings.ModelStatusMissing,
            SttModelStatus.Failed => Strings.Format("ModelStatusFailedFormat", state.Message),
            _ => state.Status.ToString()
        };
    }

    internal static string Describe(DictationState state) => state switch
    {
        DictationState.Idle => Strings.StateIdle,
        DictationState.Recording => Strings.StateRecording,
        DictationState.Transcribing => Strings.StateTranscribing,
        DictationState.Processing => Strings.StateProcessing,
        DictationState.Pasting => Strings.StatePasting,
        DictationState.Error => Strings.StateError,
        _ => state.ToString()
    };

    /// <summary>Turns a pipeline message kind into localised text plus its technical detail.</summary>
    internal static string DescribeMessage(DictationStateChangedEventArgs e) => e.Kind switch
    {
        DictationMessageKind.RecordingTooShort => Strings.MessageRecordingTooShort,
        DictationMessageKind.DictationCancelled => Strings.MessageDictationCancelled,
        DictationMessageKind.NoTextRecognized => Strings.MessageNoTextRecognized,
        DictationMessageKind.LlmFallback => Strings.MessageLlmFallback,
        DictationMessageKind.MicrophoneFailed => Strings.Format("MessageMicrophoneFailedFormat", e.Detail),
        DictationMessageKind.DictationFailed => Strings.Format("MessageDictationFailedFormat", e.Detail),
        DictationMessageKind.PasteFailed => Strings.Format(
            "MessageDeliveryFailedFormat",
            e.Detail ?? Strings.UnknownError),
        _ => string.Empty
    };

    private static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _pipeline.StateChanged -= OnStateChanged;
        _pipeline.LevelChanged -= OnLevelChanged;
        _pipeline.Completed -= OnCompleted;
        _transcription.StateChanged -= OnModelStateChanged;
    }
}
