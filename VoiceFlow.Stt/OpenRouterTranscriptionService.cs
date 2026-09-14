using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Stt;

public sealed class OpenRouterTranscriptionService : ITranscriptionService
{
    private const string DefaultEndpoint = "https://openrouter.ai/api/v1/audio/transcriptions";
    private readonly ISettingsService _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterTranscriptionService> _logger;

    public OpenRouterTranscriptionService(
        ISettingsService settings,
        HttpClient httpClient,
        ILogger<OpenRouterTranscriptionService> logger)
    {
        _settings = settings;
        _httpClient = httpClient;
        _logger = logger;
    }

    public SttModelState State { get; private set; } =
        new(SttModelStatus.Ready, "Nube OpenRouter lista");

    public event EventHandler<SttModelState>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var model = _settings.Current.Stt.CloudModel;
        var hasKey = !string.IsNullOrWhiteSpace(_settings.GetApiKey());

        State = hasKey
            ? new SttModelState(SttModelStatus.Ready, $"Nube OpenRouter ({model})")
            : new SttModelState(SttModelStatus.Failed, "Falta la clave API de OpenRouter");

        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        RecordedAudio audio,
        CancellationToken cancellationToken = default)
    {
        if (audio.IsEmpty)
        {
            return TranscriptionResult.Empty;
        }

        var apiKey = _settings.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenRouter transcription requested without an API key");
            throw new InvalidOperationException("No hay clave de API configurada para OpenRouter.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.Current.Stt.CloudModel)
            ? CloudSttCatalog.DefaultModelId
            : _settings.Current.Stt.CloudModel;

        var stopwatch = Stopwatch.StartNew();
        var wavBytes = audio.ToWavBytes();

        var boundary = "----VoicePod" + Guid.NewGuid().ToString("N");
        using var content = new MultipartFormDataContent(boundary);
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");

        using var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = "\"audio.wav\""
        };
        content.Add(fileContent);

        var modelBytes = System.Text.Encoding.UTF8.GetBytes(model);
        using var modelContent = new ByteArrayContent(modelBytes);
        modelContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"model\""
        };
        content.Add(modelContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/cisternasfelipe/voicepod");
        request.Headers.TryAddWithoutValidation("X-Title", "VoicePod");
        request.Headers.TryAddWithoutValidation("User-Agent", "VoicePod/1.0");
        request.Content = content;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.Current.Llm.TimeoutSeconds));

        var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenRouter STT failed ({StatusCode}): {Body}", response.StatusCode, body);
            throw new HttpRequestException($"OpenRouter STT error ({(int)response.StatusCode}): {body}");
        }

        stopwatch.Stop();

        using var doc = JsonDocument.Parse(body);
        var text = string.Empty;

        if (doc.RootElement.TryGetProperty("text", out var textProp))
        {
            text = textProp.GetString() ?? string.Empty;
        }

        _logger.LogInformation(
            "OpenRouter STT with {Model} finished in {Elapsed} ms. Text length: {Length}",
            model,
            stopwatch.ElapsedMilliseconds,
            text.Length);

        return new TranscriptionResult(text.Trim(), (int)stopwatch.ElapsedMilliseconds);
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        InitializeAsync(cancellationToken);

    public void Dispose()
    {
    }
}
