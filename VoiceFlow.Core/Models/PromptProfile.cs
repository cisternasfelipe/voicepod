namespace VoiceFlow.Core.Models;

/// <summary>
/// A named post-processing recipe: the system prompt handed to the LLM plus the
/// model overrides used for it. A profile with <see cref="UsesLlm"/> disabled pastes
/// the raw transcript untouched.
/// </summary>
public sealed class PromptProfile
{
    public const string TranscriptPlaceholder = "{transcript}";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Nuevo perfil";

    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Empty means "use the model configured in the AI settings".</summary>
    public string? Model { get; set; }

    public double? Temperature { get; set; }

    /// <summary>False for the built-in "Crudo" profile, which never calls the LLM.</summary>
    public bool UsesLlm { get; set; } = true;

    /// <summary>Seed profiles can be restored to their original prompt from the editor.</summary>
    public string? BuiltInKey { get; set; }

    public bool IsBuiltIn => !string.IsNullOrEmpty(BuiltInKey);

    public bool UsesTranscriptPlaceholder =>
        SystemPrompt.Contains(TranscriptPlaceholder, StringComparison.OrdinalIgnoreCase);

    public PromptProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        SystemPrompt = SystemPrompt,
        Model = Model,
        Temperature = Temperature,
        UsesLlm = UsesLlm,
        BuiltInKey = BuiltInKey
    };
}
