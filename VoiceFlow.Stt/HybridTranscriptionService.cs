using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Stt;

/// <summary>
/// Routes transcription requests between OpenRouter Cloud STT and local Sherpa-ONNX.
/// When Cloud is selected, falls back to local STT seamlessly if the cloud is unreachable.
/// </summary>
public sealed class HybridTranscriptionService : ITranscriptionService
{
    private readonly ISettingsService _settings;
    private readonly SherpaTranscriptionService _localService;
    private readonly OpenRouterTranscriptionService _cloudService;
    private readonly ILogger<HybridTranscriptionService> _logger;

    public HybridTranscriptionService(
        ISettingsService settings,
        SherpaTranscriptionService localService,
        OpenRouterTranscriptionService cloudService,
        ILogger<HybridTranscriptionService> logger)
    {
        _settings = settings;
        _localService = localService;
        _cloudService = cloudService;
        _logger = logger;

        _localService.StateChanged += (_, e) =>
        {
            if (_settings.Current.Stt.Provider != SttProvider.OpenRouterCloud)
            {
                StateChanged?.Invoke(this, e);
            }
        };

        _cloudService.StateChanged += (_, e) =>
        {
            if (_settings.Current.Stt.Provider == SttProvider.OpenRouterCloud)
            {
                StateChanged?.Invoke(this, e);
            }
        };

        _settings.SettingsChanged += (_, _) =>
        {
            StateChanged?.Invoke(this, State);
        };
    }

    public SttModelState State => _settings.Current.Stt.Provider == SttProvider.OpenRouterCloud
        ? _cloudService.State
        : _localService.State;

    public event EventHandler<SttModelState>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.Current.Stt.Provider == SttProvider.OpenRouterCloud)
        {
            await _cloudService.InitializeAsync(cancellationToken).ConfigureAwait(false);
            // Also initialize local service in background as fallback if model files are present
            _ = Task.Run(() => _localService.InitializeAsync(CancellationToken.None), CancellationToken.None);
        }
        else
        {
            await _localService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        RecordedAudio audio,
        CancellationToken cancellationToken = default)
    {
        if (_settings.Current.Stt.Provider == SttProvider.OpenRouterCloud)
        {
            try
            {
                return await _cloudService.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter Cloud STT failed; attempting fallback to local recognizer");

                // If local recognizer is ready, use it as fallback
                if (_localService.State.IsReady)
                {
                    return await _localService.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
                }

                throw;
            }
        }

        return await _localService.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _cloudService.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await _localService.ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _localService.Dispose();
        _cloudService.Dispose();
    }
}
