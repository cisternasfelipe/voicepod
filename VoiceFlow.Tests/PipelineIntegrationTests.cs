using Microsoft.Extensions.Logging.Abstractions;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Core.Pipeline;
using VoiceFlow.Storage;
using VoiceFlow.Stt;
using Xunit;
using Xunit.Abstractions;

namespace VoiceFlow.Tests;

/// <summary>
/// Drives the whole pipeline with a canned recording: transcription is real, the LLM is
/// deliberately unreachable, and the result still has to be pasted and stored.
/// </summary>
public class PipelineIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"voiceflow-test-{Guid.NewGuid():N}.db");

    public PipelineIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task FallsBackToTheRawTranscriptAndRecordsTheFailure()
    {
        if (!SherpaTranscriptionService.ModelFilesExist(AppPaths.DefaultModelDirectory, out var missing))
        {
            _output.WriteLine($"Model not present ({missing}); skipping.");
            return;
        }

        var settings = new FakeSettings();
        settings.Current.Audio.MinRecordingMilliseconds = 0;

        var audio = new FakeAudioCapture(TestAudio.LoadSpanishSample());
        var hotkey = new FakeHotkey();
        var paste = new FakePaste();
        using var transcription = new SherpaTranscriptionService(settings, NullLogger<SherpaTranscriptionService>.Instance);
        var history = new SqliteHistoryRepository(NullLogger<SqliteHistoryRepository>.Instance, _databasePath);
        await history.InitializeAsync();

        // Unreachable endpoint: the post-processing must fail without losing the dictation.
        settings.Current.Llm.BaseUrl = "http://127.0.0.1:9/v1";
        settings.Current.Llm.TimeoutSeconds = 5;
        var llm = new Llm.OpenAiCompatibleClient(
            new HttpClient(),
            settings,
            NullLogger<Llm.OpenAiCompatibleClient>.Instance);

        using var pipeline = new DictationPipeline(
            settings,
            audio,
            hotkey,
            new FakeForegroundWindow(),
            transcription,
            llm,
            paste,
            history,
            NullLogger<DictationPipeline>.Instance);

        var completed = new TaskCompletionSource<DictationCompletedEventArgs>();
        pipeline.Completed += (_, e) => completed.TrySetResult(e);

        pipeline.StartRecording();
        await pipeline.StopRecordingAsync();

        var result = await completed.Task.WaitAsync(TimeSpan.FromMinutes(2));

        _output.WriteLine($"raw: {result.RawTranscript}");
        _output.WriteLine($"processed: {result.ProcessedText}");
        _output.WriteLine($"llm error: {result.LlmError}");

        Assert.False(string.IsNullOrWhiteSpace(result.RawTranscript));
        Assert.Equal(result.RawTranscript, result.ProcessedText);
        Assert.NotNull(result.LlmError);
        Assert.Equal(result.RawTranscript, paste.LastText);

        var stored = await history.QueryAsync(new HistoryQuery());
        var entry = Assert.Single(stored);
        Assert.Equal(result.RawTranscript, entry.RawTranscript);
        Assert.Equal(result.RawTranscript, entry.ProcessedText);
        Assert.NotNull(entry.LlmError);
        Assert.True(entry.PasteSucceeded);
        Assert.True(entry.SttLatencyMs > 0);
    }

    [Fact]
    public async Task DiscardsRecordingsShorterThanTheMinimum()
    {
        var settings = new FakeSettings();
        settings.Current.Audio.MinRecordingMilliseconds = 5_000;

        var audio = new FakeAudioCapture(new RecordedAudio(new float[16_000]));
        var paste = new FakePaste();
        var history = new SqliteHistoryRepository(NullLogger<SqliteHistoryRepository>.Instance, _databasePath);
        await history.InitializeAsync();

        using var pipeline = new DictationPipeline(
            settings,
            audio,
            new FakeHotkey(),
            new FakeForegroundWindow(),
            new ThrowingTranscription(),
            new ThrowingLlm(),
            paste,
            history,
            NullLogger<DictationPipeline>.Instance);

        pipeline.StartRecording();
        await pipeline.StopRecordingAsync();

        Assert.Null(paste.LastText);
        Assert.Empty(await history.QueryAsync(new HistoryQuery()));
        Assert.Equal(DictationState.Idle, pipeline.State);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public string? GetApiKey() => "test-key";

        public void SetApiKey(string? apiKey)
        {
        }
    }

    private sealed class FakeAudioCapture : IAudioCaptureService
    {
        private readonly RecordedAudio _audio;

        public FakeAudioCapture(RecordedAudio audio) => _audio = audio;

        public bool IsRecording { get; private set; }

        public event EventHandler<AudioLevelEventArgs>? LevelChanged;

        public event EventHandler? MaxDurationReached;

        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [new("fake", "Fake device", true)];

        public void Prepare(string? deviceId)
        {
        }

        public void Start(string? deviceId, TimeSpan maxDuration)
        {
            IsRecording = true;
            LevelChanged?.Invoke(this, new AudioLevelEventArgs(0.5f, 0.3f));
        }

        public RecordedAudio Stop()
        {
            IsRecording = false;
            return _audio;
        }

        public void Cancel() => IsRecording = false;

        public void Dispose() => MaxDurationReached?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeHotkey : IGlobalHotkeyService
    {
        public HotkeyDefinition? Current => HotkeyDefinition.Default;

        // The pipeline subscribes to these; the tests drive it directly instead.
        public event EventHandler? Pressed
        {
            add { }
            remove { }
        }

        public event EventHandler? Released
        {
            add { }
            remove { }
        }

        public event EventHandler? HoldPressed
        {
            add { }
            remove { }
        }

        public event EventHandler? HoldReleased
        {
            add { }
            remove { }
        }

        public event EventHandler? TogglePressed
        {
            add { }
            remove { }
        }

        public HotkeyRegistrationResult Register(HotkeyDefinition definition, HotkeyMode mode) =>
            HotkeyRegistrationResult.Ok();

        public void Unregister()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeForegroundWindow : IForegroundWindowProvider
    {
        public nint GetForegroundWindow() => 1234;

        public bool IsWindow(nint handle) => true;

        public string GetWindowTitle(nint handle) => "Ventana de prueba";
    }

    private sealed class FakePaste : IPasteService
    {
        public string? LastText { get; private set; }

        public Task<PasteResult> PasteAsync(
            string text,
            nint targetWindow,
            PasteSettings settings,
            CancellationToken cancellationToken = default)
        {
            LastText = text;
            return Task.FromResult(new PasteResult(PasteOutcome.Pasted));
        }
    }

    private sealed class ThrowingTranscription : ITranscriptionService
    {
        public SttModelState State => new(SttModelStatus.Ready);

        public event EventHandler<SttModelState>? StateChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TranscriptionResult> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Should not be reached for a discarded recording.");

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLlm : ILlmClient
    {
        public Task<LlmResult> ProcessAsync(PromptProfile profile, string transcript, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Should not be reached for a discarded recording.");

        public Task<LlmConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmConnectionResult(false, Error: "no"));

        public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);
    }
}
