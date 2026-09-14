using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Pipeline;

public sealed class DictationCompletedEventArgs : EventArgs
{
    public required RecordedAudio Audio { get; init; }

    public string? AudioPath { get; init; }

    public int DurationMs { get; init; }

    public string RawTranscript { get; init; } = string.Empty;

    public int SttLatencyMs { get; init; }

    /// <summary>Text after post-processing; equal to the transcript when the LLM is skipped or fails.</summary>
    public string ProcessedText { get; init; } = string.Empty;

    public int LlmLatencyMs { get; init; }

    public string ProfileName { get; init; } = string.Empty;

    public string ModelUsed { get; init; } = string.Empty;

    public string? LlmError { get; init; }

    public PasteOutcome PasteOutcome { get; init; }

    public string? PasteError { get; init; }
}

/// <summary>
/// Drives the dictation state machine. Phase 1 covers hotkey to recording to WAV;
/// transcription, post-processing and pasting hook into the same flow in later phases.
/// </summary>
public sealed class DictationPipeline : IDisposable
{
    /// <summary>Value stored in the history when the profile skips the LLM.</summary>
    public const string NoLlmModelName = "no-llm";

    private readonly ISettingsService _settings;
    private readonly IAudioCaptureService _audio;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly IForegroundWindowProvider _foreground;
    private readonly ITranscriptionService _transcription;
    private readonly ILlmClient _llm;
    private readonly IPasteService _paste;
    private readonly IHistoryRepository _history;
    private readonly ILogger<DictationPipeline> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Stopwatch? _recordingClock;
    private nint _targetWindow;
    private bool _disposed;

    public DictationPipeline(
        ISettingsService settings,
        IAudioCaptureService audio,
        IGlobalHotkeyService hotkey,
        IForegroundWindowProvider foreground,
        ITranscriptionService transcription,
        ILlmClient llm,
        IPasteService paste,
        IHistoryRepository history,
        ILogger<DictationPipeline> logger)
    {
        _settings = settings;
        _audio = audio;
        _hotkey = hotkey;
        _foreground = foreground;
        _transcription = transcription;
        _llm = llm;
        _paste = paste;
        _history = history;
        _logger = logger;

        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.Released += OnHotkeyReleased;
        _hotkey.HoldPressed += OnHoldPressed;
        _hotkey.HoldReleased += OnHoldReleased;
        _hotkey.TogglePressed += OnTogglePressed;
        _audio.MaxDurationReached += OnMaxDurationReached;
        _audio.LevelChanged += (_, e) => LevelChanged?.Invoke(this, e);
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public event EventHandler<DictationStateChangedEventArgs>? StateChanged;

    public event EventHandler<AudioLevelEventArgs>? LevelChanged;

    public event EventHandler<DictationCompletedEventArgs>? Completed;

    /// <summary>HWND that had focus when the recording started; the paste target.</summary>
    public nint TargetWindow => _targetWindow;

    public bool IsBusy => State != DictationState.Idle && State != DictationState.Error;

    /// <summary>Starts a dictation, or stops the running one when in toggle mode.</summary>
    public void Toggle()
    {
        if (State == DictationState.Recording)
        {
            _ = StopRecordingAsync();
        }
        else if (!IsBusy)
        {
            StartRecording();
        }
    }

    public void StartRecording()
    {
        if (IsBusy)
        {
            _logger.LogDebug("Ignoring start request while in state {State}", State);
            return;
        }

        try
        {
            _targetWindow = _foreground.GetForegroundWindow();
            var settings = _settings.Current;

            _audio.Start(
                settings.Audio.InputDeviceId,
                TimeSpan.FromSeconds(settings.Audio.MaxRecordingSeconds));

            _recordingClock = Stopwatch.StartNew();
            SetState(DictationState.Recording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start recording");
            SetState(DictationState.Error, DictationMessageKind.MicrophoneFailed, ex.Message);
            ResetToIdle();
        }
    }

    public async Task StopRecordingAsync()
    {
        if (State != DictationState.Recording)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var elapsed = _recordingClock?.ElapsedMilliseconds ?? 0;
            _recordingClock = null;

            var audio = _audio.Stop();

            // Keep the device warm so the next hotkey press starts recording immediately.
            _audio.Prepare(_settings.Current.Audio.InputDeviceId);
            var minimum = _settings.Current.Audio.MinRecordingMilliseconds;

            if (audio.IsEmpty || elapsed < minimum)
            {
                _logger.LogInformation("Recording discarded ({Elapsed} ms, minimum {Minimum} ms)", elapsed, minimum);
                SetState(DictationState.Idle, DictationMessageKind.RecordingTooShort);
                return;
            }

            await ProcessRecordingAsync(audio, (int)elapsed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed");
            SetState(DictationState.Error, DictationMessageKind.DictationFailed, ex.Message);
            ResetToIdle();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cancels the recording in progress without producing any text.</summary>
    public void CancelRecording()
    {
        if (State != DictationState.Recording)
        {
            return;
        }

        _audio.Cancel();
        _recordingClock = null;
        SetState(DictationState.Idle, DictationMessageKind.DictationCancelled);
    }

    private async Task ProcessRecordingAsync(RecordedAudio audio, int durationMs)
    {
        string? audioPath = null;

        if (AudioSink is not null)
        {
            audioPath = await AudioSink(audio).ConfigureAwait(false);
        }

        SetState(DictationState.Transcribing);
        var transcription = await _transcription.TranscribeAsync(audio).ConfigureAwait(false);

        if (transcription.IsEmpty)
        {
            _logger.LogInformation("The recognizer returned no text for a {Duration} ms recording", durationMs);
            SetState(DictationState.Idle, DictationMessageKind.NoTextRecognized);
            return;
        }

        var profile = _settings.Current.GetActiveProfile();
        var processed = transcription.Text;
        var llmLatency = 0;
        string? llmError = null;
        var modelUsed = NoLlmModelName;

        if (profile.UsesLlm)
        {
            SetState(DictationState.Processing);
            var result = await _llm.ProcessAsync(profile, transcription.Text).ConfigureAwait(false);

            llmLatency = result.LatencyMs;
            modelUsed = result.ModelUsed;

            if (result.Failed)
            {
                // Nothing is lost: the raw transcript is what gets pasted.
                llmError = result.Error;
                _logger.LogWarning("Falling back to the raw transcript: {Error}", result.Error);
                SetState(DictationState.Processing, DictationMessageKind.LlmFallback, result.Error);
            }
            else
            {
                processed = result.Text;
            }
        }

        SetState(DictationState.Pasting);
        var paste = await _paste
            .PasteAsync(processed, _targetWindow, _settings.Current.Paste)
            .ConfigureAwait(false);

        var entry = new HistoryEntry
        {
            CreatedAtUtc = DateTime.UtcNow,
            DurationMs = durationMs,
            RawTranscript = transcription.Text,
            ProcessedText = processed,
            ProfileName = profile.Name,
            ModelUsed = modelUsed,
            PasteSucceeded = paste.Succeeded,
            LlmError = llmError,
            SttLatencyMs = transcription.LatencyMs,
            LlmLatencyMs = llmLatency,
            AudioPath = audioPath
        };

        await SaveHistoryAsync(entry).ConfigureAwait(false);

        Completed?.Invoke(this, new DictationCompletedEventArgs
        {
            Audio = audio,
            AudioPath = audioPath,
            DurationMs = durationMs,
            RawTranscript = transcription.Text,
            SttLatencyMs = transcription.LatencyMs,
            ProcessedText = processed,
            LlmLatencyMs = llmLatency,
            ProfileName = profile.Name,
            ModelUsed = modelUsed,
            LlmError = llmError,
            PasteOutcome = paste.Outcome,
            PasteError = paste.Error
        });

        SetState(
            DictationState.Idle,
            paste.Succeeded ? DictationMessageKind.None : DictationMessageKind.PasteFailed,
            paste.Error);
    }

    /// <summary>
    /// Optional hook that persists the recording and returns its path. Set by the host so
    /// Core stays free of file-format concerns.
    /// </summary>
    public Func<RecordedAudio, Task<string?>>? AudioSink { get; set; }

    /// <summary>Stores the dictation. A history failure must never lose the dictation itself.</summary>
    private async Task SaveHistoryAsync(HistoryEntry entry)
    {
        try
        {
            await _history.AddAsync(entry).ConfigureAwait(false);
            await _history.ApplyRetentionAsync(_settings.Current.History).ConfigureAwait(false);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the dictation to the history database");
        }
    }

    /// <summary>Raised after a dictation is stored, so an open history window can refresh.</summary>
    public event EventHandler? HistoryChanged;

    private void OnHoldPressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Hold hotkey pressed -> StartRecording");
        StartRecording();
    }

    private void OnHoldReleased(object? sender, EventArgs e)
    {
        _logger.LogInformation("Hold hotkey released -> StopRecordingAsync");
        _ = StopRecordingAsync();
    }

    private void OnTogglePressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Toggle hotkey pressed -> Toggle");
        Toggle();
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_hotkey.HoldHotkey is null && _hotkey.ToggleHotkey is null)
        {
            if (_settings.Current.Hotkey.Mode == HotkeyMode.PushToTalk)
            {
                StartRecording();
            }
            else
            {
                Toggle();
            }
        }
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        if (_hotkey.HoldHotkey is null && _hotkey.ToggleHotkey is null)
        {
            if (_settings.Current.Hotkey.Mode == HotkeyMode.PushToTalk)
            {
                _ = StopRecordingAsync();
            }
        }
    }

    private void OnMaxDurationReached(object? sender, EventArgs e)
    {
        _logger.LogInformation("Maximum recording duration reached, stopping");
        _ = StopRecordingAsync();
    }

    private void ResetToIdle() => SetState(DictationState.Idle);

    private void SetState(
        DictationState state,
        DictationMessageKind kind = DictationMessageKind.None,
        string? detail = null)
    {
        State = state;
        StateChanged?.Invoke(this, new DictationStateChangedEventArgs(state, kind, detail));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Released -= OnHotkeyReleased;
        _hotkey.HoldPressed -= OnHoldPressed;
        _hotkey.HoldReleased -= OnHoldReleased;
        _hotkey.TogglePressed -= OnTogglePressed;
        _audio.MaxDurationReached -= OnMaxDurationReached;
        _gate.Dispose();
    }
}
