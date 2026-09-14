namespace VoiceFlow.Core.Models;

public enum LlmProviderKind
{
    OpenAi,
    OpenRouter,
    Groq,
    LmStudio,
    Ollama,
    Custom
}

/// <summary>A ready-made endpoint configuration offered in the AI settings tab.</summary>
public sealed record LlmProviderPreset(
    LlmProviderKind Kind,
    string DisplayName,
    string BaseUrl,
    string? SuggestedModel,
    string? ApiKeyUrl)
{
    public bool NeedsApiKey => Kind is not (LlmProviderKind.LmStudio or LlmProviderKind.Ollama);
}

/// <summary>
/// Known OpenAI-compatible endpoints. Only the base URL really changes; OpenRouter also
/// gets attribution headers and a key-aware connection test (see the LLM client).
/// </summary>
public static class LlmProviders
{
    public const string OpenRouterHost = "openrouter.ai";

    public const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    /// <summary>Sent to OpenRouter as HTTP-Referer/X-Title so usage shows up under the app.</summary>
    public const string AttributionUrl = "https://github.com/voiceflow-app";

    public const string AttributionTitle = "VoiceFlow";

    public static IReadOnlyList<LlmProviderPreset> All { get; } =
    [
        new(LlmProviderKind.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini",
            "https://platform.openai.com/api-keys"),
        new(LlmProviderKind.OpenRouter, "OpenRouter", OpenRouterBaseUrl, "openai/gpt-4o-mini",
            "https://openrouter.ai/keys"),
        new(LlmProviderKind.Groq, "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile",
            "https://console.groq.com/keys"),
        new(LlmProviderKind.LmStudio, "LM Studio", "http://localhost:1234/v1", null, null),
        new(LlmProviderKind.Ollama, "Ollama", "http://localhost:11434/v1", null, null)
    ];

    public static LlmProviderKind Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return LlmProviderKind.Custom;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return LlmProviderKind.Custom;
        }

        foreach (var preset in All)
        {
            if (Uri.TryCreate(preset.BaseUrl, UriKind.Absolute, out var known)
                && string.Equals(known.Host, uri.Host, StringComparison.OrdinalIgnoreCase)
                && known.Port == uri.Port)
            {
                return preset.Kind;
            }
        }

        return LlmProviderKind.Custom;
    }

    public static bool IsOpenRouter(string? baseUrl) => Detect(baseUrl) == LlmProviderKind.OpenRouter;

    public static LlmProviderPreset? Find(LlmProviderKind kind) => All.FirstOrDefault(p => p.Kind == kind);
}
