namespace VoiceFlow.Core.Models;

/// <summary>
/// Seed profiles created on first run. Their prompts stay available so the editor can
/// offer "restaurar por defecto" after the user edits one.
/// </summary>
public static class BuiltInProfiles
{
    public const string RawKey = "raw";
    public const string CleanupKey = "cleanup";
    public const string FormalEmailKey = "formal-email";
    public const string AgentPromptKey = "agent-prompt";

    public const string CleanupPrompt =
        "Eres un procesador de dictado por voz para Windows. Tu ÚNICA función es devolver el texto dictado por el usuario, corregido y limpio.\n\n" +
        "REGLAS ESTRICTAS DE SALIDA:\n" +
        "1. Devuelve EXCLUSIVAMENTE el texto final resultante. Ni una sola palabra más.\n" +
        "2. PROHIBIDO terminantemente incluir saludos, preámbulos o confirmaciones (NUNCA digas 'Aquí está...', 'Texto corregido:', etc.).\n" +
        "3. PROHIBIDO incluir notas, listas de correcciones, explicaciones o comentarios (NUNCA agregues secciones como 'Notas sobre las correcciones:' ni advertencias de contexto).\n" +
        "4. NO envuelvas el resultado entre comillas ni bloques de código.\n\n" +
        "DEDUCCIÓN FONÉTICA Y DE CONTEXTO:\n" +
        "- El texto proviene de un reconocedor de voz offline que a menudo confunde consonantes o palabras fonéticamente similares (por ejemplo: si el audio dice 'batalla' pero el contexto habla de ventanas, minimizar, software o diseño, la palabra real es 'pantalla'; 'agramos' es 'agregamos' o 'agregar'; 'emocionada' es 'emocionante').\n" +
        "- Deduce las palabras correctas según la coherencia lógica de la frase.\n" +
        "- Corrige puntuación, mayúsculas y suprime muletillas (eh, em, este, o sea) preservando exactamente la intención del usuario.";

    public const string FormalEmailPrompt =
        "Eres un asistente de redacción profesional. Reescribe el dictado del usuario como un " +
        "correo electrónico formal en español, con saludo, cuerpo bien estructurado en párrafos " +
        "y despedida. Mantén toda la información del dictado, no inventes datos que no aparezcan " +
        "y usa un tono cortés y profesional. Si falta el destinatario, usa un saludo genérico. " +
        "Devuelve ÚNICAMENTE el texto del correo, sin asunto salvo que el dictado lo mencione y " +
        "sin explicaciones.";

    public const string AgentPromptPrompt =
        "Eres un ingeniero que traduce peticiones habladas en instrucciones técnicas para un " +
        "agente de programación. Reescribe el dictado como una instrucción estructurada y " +
        "accionable en español, con: objetivo en una frase, requisitos concretos en viñetas y, " +
        "si el dictado los menciona, restricciones y criterios de aceptación. No inventes " +
        "requisitos que no estén en el dictado. Devuelve ÚNICAMENTE la instrucción resultante, " +
        "sin explicaciones ni preámbulos.";

    public static IReadOnlyDictionary<string, string> DefaultPrompts { get; } =
        new Dictionary<string, string>
        {
            [RawKey] = string.Empty,
            [CleanupKey] = CleanupPrompt,
            [FormalEmailKey] = FormalEmailPrompt,
            [AgentPromptKey] = AgentPromptPrompt
        };

    public static List<PromptProfile> CreateSeed() =>
    [
        new PromptProfile
        {
            Id = "profile-raw",
            Name = "Crudo",
            BuiltInKey = RawKey,
            UsesLlm = false,
            SystemPrompt = string.Empty
        },
        new PromptProfile
        {
            Id = "profile-cleanup",
            Name = "Limpieza",
            BuiltInKey = CleanupKey,
            SystemPrompt = CleanupPrompt,
            Temperature = 0.2
        },
        new PromptProfile
        {
            Id = "profile-formal-email",
            Name = "Correo formal",
            BuiltInKey = FormalEmailKey,
            SystemPrompt = FormalEmailPrompt,
            Temperature = 0.4
        },
        new PromptProfile
        {
            Id = "profile-agent-prompt",
            Name = "Prompt para agente",
            BuiltInKey = AgentPromptKey,
            SystemPrompt = AgentPromptPrompt,
            Temperature = 0.3
        }
    ];
}
