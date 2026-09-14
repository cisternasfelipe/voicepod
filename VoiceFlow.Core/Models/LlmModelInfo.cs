namespace VoiceFlow.Core.Models;

/// <summary>
/// One entry of the endpoint model catalogue. Everything but <see cref="Id"/> is optional:
/// plain OpenAI-compatible servers only publish ids, while OpenRouter also reports names,
/// context length and prices.
/// </summary>
public sealed record LlmModelInfo(
    string Id,
    string? Name = null,
    int? ContextLength = null,
    decimal? PromptPricePerMillion = null,
    decimal? CompletionPricePerMillion = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;

    /// <summary>True for OpenRouter entries that cost nothing (the ":free" variants).</summary>
    public bool IsFree =>
        Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
        || (PromptPricePerMillion == 0 && CompletionPricePerMillion == 0);

    /// <summary>Short "128k ctx · $0.15/$0.60 por 1M" style summary; empty when nothing is known.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(2);

            if (ContextLength is > 0)
            {
                parts.Add(ContextLength >= 1000
                    ? $"{ContextLength / 1000}k ctx"
                    : $"{ContextLength} ctx");
            }

            if (IsFree)
            {
                parts.Add("free");
            }
            else if (PromptPricePerMillion is not null || CompletionPricePerMillion is not null)
            {
                var prompt = PromptPricePerMillion ?? 0;
                var completion = CompletionPricePerMillion ?? 0;
                parts.Add($"${prompt:0.##}/${completion:0.##} 1M");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Outcome of the "Probar conexión" button.</summary>
public sealed record LlmConnectionResult(bool Success, string? Detail = null, string? Error = null);
