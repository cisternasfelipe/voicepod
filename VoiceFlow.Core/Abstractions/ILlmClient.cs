using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

public interface ILlmClient
{
    /// <summary>
    /// Runs the transcript through the profile's prompt. Never throws for transport or
    /// API errors: the failure is reported in <see cref="LlmResult.Error"/> so the caller
    /// can still paste the raw transcript.
    /// </summary>
    Task<LlmResult> ProcessAsync(
        PromptProfile profile,
        string transcript,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Used by the "Probar conexión" button. On OpenRouter this validates the key itself
    /// (its /models endpoint answers even without one) and reports the remaining credit.
    /// </summary>
    Task<LlmConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Model catalogue published by the endpoint. Returns an empty list when the server has
    /// no /models endpoint; throws only for transport failures the caller should surface.
    /// </summary>
    Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);
}
