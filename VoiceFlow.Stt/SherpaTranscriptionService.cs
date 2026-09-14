using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SherpaOnnx;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Stt;

/// <summary>
/// Offline transcription with sherpa-onnx running NVIDIA Parakeet TDT 0.6B v3 (INT8) as a
/// NeMo transducer. The recognizer is loaded once and reused for every dictation.
/// </summary>
public sealed class SherpaTranscriptionService : ITranscriptionService
{
    private const string EncoderFile = "encoder.int8.onnx";
    private const string DecoderFile = "decoder.int8.onnx";
    private const string JoinerFile = "joiner.int8.onnx";
    private const string TokensFile = "tokens.txt";

    private readonly ISettingsService _settings;
    private readonly ILogger<SherpaTranscriptionService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    // sherpa recognizers are not documented as thread safe: serialise decoding.
    private readonly SemaphoreSlim _decodeLock = new(1, 1);

    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    public SherpaTranscriptionService(ISettingsService settings, ILogger<SherpaTranscriptionService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public SttModelState State { get; private set; } = SttModelState.NotDownloaded;

    public event EventHandler<SttModelState>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer is not null)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_recognizer is not null)
            {
                return;
            }

            var directory = ResolveModelDirectory();

            if (!ModelFilesExist(directory, out var missing))
            {
                SetState(new SttModelState(
                    SttModelStatus.NotDownloaded,
                    $"Falta el archivo del modelo: {missing}"));
                return;
            }

            SetState(new SttModelState(SttModelStatus.Loading, "Cargando el modelo en memoria..."));

            var stopwatch = Stopwatch.StartNew();
            var settings = _settings.Current.Stt;

            // Loading is CPU bound and takes seconds: keep it off the UI thread.
            _recognizer = await Task.Run(() => CreateRecognizer(directory, settings), cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Parakeet model loaded from {Directory} in {Elapsed} ms ({Threads} threads, {Provider})",
                directory,
                stopwatch.ElapsedMilliseconds,
                settings.NumThreads,
                settings.Provider);

            SetState(new SttModelState(SttModelStatus.Ready));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the transcription model");
            SetState(new SttModelState(SttModelStatus.Failed, "No se pudo cargar el modelo: " + ex.Message));
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        RecordedAudio audio,
        CancellationToken cancellationToken = default)
    {
        if (audio.IsEmpty)
        {
            return TranscriptionResult.Empty;
        }

        if (_recognizer is null)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var recognizer = _recognizer
            ?? throw new InvalidOperationException("El modelo de transcripción no está disponible.");

        await _decodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var text = await Task.Run(
                () =>
                {
                    using var stream = recognizer.CreateStream();
                    stream.AcceptWaveform(audio.SampleRate, audio.Samples);
                    recognizer.Decode(stream);
                    return stream.Result.Text;
                },
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "Transcribed {Seconds:0.00} s of audio in {Latency} ms",
                audio.Duration.TotalSeconds,
                latency);

            return new TranscriptionResult(text.Trim(), latency);
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            SetState(new SttModelState(SttModelStatus.NotDownloaded));
        }
        finally
        {
            _loadLock.Release();
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private OfflineRecognizer CreateRecognizer(string directory, SttSettings settings)
    {
        var config = new OfflineRecognizerConfig();

        config.FeatConfig.SampleRate = RecordedAudio.TargetSampleRate;
        config.FeatConfig.FeatureDim = 80;

        config.ModelConfig.Transducer.Encoder = Path.Combine(directory, EncoderFile);
        config.ModelConfig.Transducer.Decoder = Path.Combine(directory, DecoderFile);
        config.ModelConfig.Transducer.Joiner = Path.Combine(directory, JoinerFile);
        config.ModelConfig.Tokens = Path.Combine(directory, TokensFile);
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = settings.NumThreads;
        config.ModelConfig.Provider = ToProviderName(settings.Provider);
        config.ModelConfig.Debug = 0;

        config.DecodingMethod = "greedy_search";

        return new OfflineRecognizer(config);
    }

    private static string ToProviderName(SttProvider provider) => provider switch
    {
        SttProvider.DirectMl => "directml",
        SttProvider.Cuda => "cuda",
        _ => "cpu"
    };

    public string ResolveModelDirectory()
    {
        var configured = _settings.Current.Stt.ModelDirectory;
        return string.IsNullOrWhiteSpace(configured) ? AppPaths.DefaultModelDirectory : configured;
    }

    public static bool ModelFilesExist(string directory, out string missingFile)
    {
        foreach (var name in new[] { EncoderFile, DecoderFile, JoinerFile, TokensFile })
        {
            if (!File.Exists(Path.Combine(directory, name)))
            {
                missingFile = name;
                return false;
            }
        }

        missingFile = string.Empty;
        return true;
    }

    private void SetState(SttModelState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recognizer?.Dispose();
        _recognizer = null;
        _loadLock.Dispose();
        _decodeLock.Dispose();
    }
}
