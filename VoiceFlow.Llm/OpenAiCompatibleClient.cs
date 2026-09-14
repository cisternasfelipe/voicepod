using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Llm;

/// <summary>
/// Chat-completions client that works against any OpenAI-compatible endpoint: OpenAI,
/// OpenRouter, Groq, an Anthropic-compatible gateway or a local LM Studio / Ollama server.
/// Only the base URL changes; OpenRouter additionally gets its attribution headers and a
/// key-aware connection test.
/// </summary>
public sealed class OpenAiCompatibleClient : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<OpenAiCompatibleClient> _logger;

    public OpenAiCompatibleClient(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<OpenAiCompatibleClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<LlmResult> ProcessAsync(
        PromptProfile profile,
        string transcript,
        CancellationToken cancellationToken = default)
    {
        var llm = _settings.Current.Llm;
        var model = string.IsNullOrWhiteSpace(profile.Model) ? llm.Model : profile.Model!;
        var stopwatch = Stopwatch.StartNew();

        if (!profile.UsesLlm)
        {
            return new LlmResult(transcript, 0, "no-llm");
        }

        try
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Temperature = profile.Temperature ?? llm.Temperature,
                MaxTokens = llm.MaxTokens,
                Messages = BuildMessages(profile, transcript)
            };

            if (llm.EnableReasoning)
            {
                var effort = string.IsNullOrWhiteSpace(llm.ReasoningEffort) ? "low" : llm.ReasoningEffort;
                request.ReasoningEffort = effort;
                request.Reasoning = new ChatReasoningOptions
                {
                    Effort = effort,
                    Exclude = false
                };
            }
            else
            {
                // Turn off reasoning for fast, economical dictation on models that reason by default
                request.ReasoningEffort = "none";
                request.Reasoning = new ChatReasoningOptions
                {
                    Effort = "none",
                    MaxTokens = 0
                };
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));

            using var httpRequest = CreateRequest(HttpMethod.Post, "chat/completions", llm);
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            using var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = ExtractErrorMessage(body) ?? response.ReasonPhrase ?? "error desconocido";
                return Failure(stopwatch, model, $"HTTP {(int)response.StatusCode}: {detail}");
            }

            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
            var rawText = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            var text = CleanReasoningTags(rawText ?? string.Empty);

            if (string.IsNullOrWhiteSpace(text))
            {
                // OpenRouter reports some upstream problems with a 200 plus an error object.
                return Failure(
                    stopwatch,
                    model,
                    parsed?.Error?.Message ?? "La respuesta del modelo llegó vacía.");
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "LLM post-processing with {Model} took {Latency} ms",
                model,
                stopwatch.ElapsedMilliseconds);

            return new LlmResult(text.Trim(), (int)stopwatch.ElapsedMilliseconds, parsed?.Model ?? model);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(stopwatch, model, $"Tiempo de espera agotado ({llm.TimeoutSeconds} s).");
        }
        catch (OperationCanceledException)
        {
            return Failure(stopwatch, model, "Petición cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM request failed");
            return Failure(stopwatch, model, ex.Message);
        }
    }

    public async Task<LlmConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var llm = _settings.Current.Llm;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));

            // OpenRouter answers /models even without credentials, so a key has to be
            // checked against /key, which also reports how much credit is left.
            if (LlmProviders.IsOpenRouter(llm.BaseUrl))
            {
                return await TestOpenRouterKeyAsync(llm, timeout.Token).ConfigureAwait(false);
            }

            // /models is the cheapest endpoint most compatible servers expose.
            using var request = CreateRequest(HttpMethod.Get, "models", llm);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new LlmConnectionResult(true);
            }

            if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.MethodNotAllowed)
            {
                // Gateways without /models still answer a tiny completion.
                return await TestWithCompletionAsync(llm, timeout.Token).ConfigureAwait(false);
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return new LlmConnectionResult(
                false,
                Error: $"HTTP {(int)response.StatusCode}: {ExtractErrorMessage(body) ?? response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LlmConnectionResult(false, Error: $"Tiempo de espera agotado ({llm.TimeoutSeconds} s).");
        }
        catch (Exception ex)
        {
            return new LlmConnectionResult(false, Error: ex.Message);
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var llm = _settings.Current.Llm;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(llm.TimeoutSeconds, 30)));

        using var request = CreateRequest(HttpMethod.Get, "models", llm);
        using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode}: {ExtractErrorMessage(body) ?? response.ReasonPhrase}");
        }

        return ParseModels(body);
    }

    /// <summary>
    /// Reads a /models payload. Handles the plain OpenAI shape (ids only) and the richer
    /// OpenRouter one (name, context length and per-token prices).
    /// </summary>
    internal static IReadOnlyList<LlmModelInfo> ParseModels(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<LlmModelInfo>(data.GetArrayLength());

        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string? name = null;
            if (entry.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString();
            }

            int? context = null;
            if (entry.TryGetProperty("context_length", out var contextElement)
                && contextElement.ValueKind == JsonValueKind.Number
                && contextElement.TryGetInt32(out var contextValue))
            {
                context = contextValue;
            }

            decimal? promptPrice = null;
            decimal? completionPrice = null;

            if (entry.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
            {
                promptPrice = ReadPricePerMillion(pricing, "prompt");
                completionPrice = ReadPricePerMillion(pricing, "completion");
            }

            models.Add(new LlmModelInfo(id!, name, context, promptPrice, completionPrice));
        }

        return models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>OpenRouter quotes prices per token as strings; the UI shows them per million.</summary>
    private static decimal? ReadPricePerMillion(JsonElement pricing, string field)
    {
        if (!pricing.TryGetProperty(field, out var value))
        {
            return null;
        }

        var raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw)
            || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken))
        {
            return null;
        }

        return perToken * 1_000_000m;
    }

    private async Task<LlmConnectionResult> TestOpenRouterKeyAsync(
        LlmSettings llm,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "key", llm);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new LlmConnectionResult(
                false,
                Error: $"HTTP {(int)response.StatusCode}: {ExtractErrorMessage(body) ?? response.ReasonPhrase}");
        }

        return new LlmConnectionResult(true, DescribeOpenRouterKey(body));
    }

    /// <summary>Turns the /key payload into a short "label · credit" line.</summary>
    internal static string? DescribeOpenRouterKey(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var parts = new List<string>(3);

            if (data.TryGetProperty("label", out var label)
                && label.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(label.GetString()))
            {
                parts.Add(label.GetString()!);
            }

            if (data.TryGetProperty("limit_remaining", out var remaining)
                && remaining.ValueKind == JsonValueKind.Number
                && remaining.TryGetDecimal(out var remainingValue))
            {
                parts.Add($"${remainingValue:0.##} restantes");
            }
            else if (data.TryGetProperty("usage", out var usage)
                     && usage.ValueKind == JsonValueKind.Number
                     && usage.TryGetDecimal(out var usageValue))
            {
                parts.Add($"${usageValue:0.##} usados");
            }

            if (data.TryGetProperty("is_free_tier", out var freeTier)
                && freeTier.ValueKind == JsonValueKind.True)
            {
                parts.Add("free tier");
            }

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<LlmConnectionResult> TestWithCompletionAsync(
        LlmSettings llm,
        CancellationToken cancellationToken)
    {
        var probe = new ChatCompletionRequest
        {
            Model = llm.Model,
            Temperature = 0,
            MaxTokens = 8,
            Messages = [new ChatMessage("user", "ping")]
        };

        using var request = CreateRequest(HttpMethod.Post, "chat/completions", llm);
        request.Content = JsonContent.Create(probe, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? new LlmConnectionResult(true)
            : new LlmConnectionResult(
                false,
                Error: $"HTTP {(int)response.StatusCode}: {ExtractErrorMessage(body) ?? response.ReasonPhrase}");
    }

    /// <summary>
    /// System prompt plus the raw transcript as the user message, unless the prompt carries
    /// a {transcript} placeholder: then the substituted text is sent as the user message.
    /// </summary>
    internal static List<ChatMessage> BuildMessages(PromptProfile profile, string transcript)
    {
        if (profile.UsesTranscriptPlaceholder)
        {
            var filled = profile.SystemPrompt.Replace(
                PromptProfile.TranscriptPlaceholder,
                transcript,
                StringComparison.OrdinalIgnoreCase);

            return [new ChatMessage("user", filled)];
        }

        return
        [
            new ChatMessage("system", profile.SystemPrompt),
            new ChatMessage("user", transcript)
        ];
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, LlmSettings llm)
    {
        var request = new HttpRequestMessage(method, BuildUri(llm.BaseUrl, relativePath));
        var apiKey = _settings.GetApiKey();
        var isOpenRouter = LlmProviders.IsOpenRouter(llm.BaseUrl);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            if (!isOpenRouter)
            {
                // Some gateways read this header instead; OpenRouter only wants the bearer.
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            }
        }

        if (isOpenRouter)
        {
            // Optional attribution headers: they file the traffic under this app on
            // openrouter.ai instead of leaving it unattributed.
            request.Headers.TryAddWithoutValidation("HTTP-Referer", LlmProviders.AttributionUrl);
            request.Headers.TryAddWithoutValidation("X-Title", LlmProviders.AttributionTitle);
        }

        return request;
    }

    internal static Uri BuildUri(string baseUrl, string relativePath)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1"
            : baseUrl.Trim().TrimEnd('/');

        return new Uri($"{trimmed}/{relativePath}");
    }

    internal static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);

            if (!string.IsNullOrWhiteSpace(parsed?.Error?.Message))
            {
                return parsed!.Error!.Message;
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the raw body.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    internal static string CleanReasoningTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Strip <think>...</think> and <thought>...</thought> tags and their contents
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"<(think|thought)>[\s\S]*?</\1>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    private LlmResult Failure(Stopwatch stopwatch, string model, string error)
    {
        stopwatch.Stop();
        _logger.LogWarning("LLM post-processing failed: {Error}", error);
        return new LlmResult(string.Empty, (int)stopwatch.ElapsedMilliseconds, model, error);
    }
}
