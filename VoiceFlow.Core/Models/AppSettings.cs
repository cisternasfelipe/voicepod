namespace VoiceFlow.Core.Models;

public enum UiLanguage
{
    Spanish,
    English
}

public enum PasteMode
{
    /// <summary>Clipboard + Ctrl+V, the fast path.</summary>
    Clipboard,

    /// <summary>Unicode SendInput, character by character, for apps that block pasting.</summary>
    TypeUnicode
}

public enum SttProvider
{
    OpenRouterCloud,
    Cpu,
    DirectMl,
    Cuda
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool PlaySounds { get; set; } = true;

    public UiLanguage Language { get; set; } = UiLanguage.Spanish;
}

public sealed class HotkeySettings
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyDefinition.Default.Modifiers;

    public int VirtualKey { get; set; } = HotkeyDefinition.Default.VirtualKey;

    public int[]? Keys { get; set; }

    public HotkeyMode Mode { get; set; } = HotkeyMode.Toggle;

    /// <summary>
    /// Dedicated hotkey for Push-to-Talk ("Mantener presionado para transcribir, soltar para parar").
    /// </summary>
    public HotkeyDefinition? HoldHotkey { get; set; }

    /// <summary>
    /// Dedicated hotkey for Toggle ("Presionar para activar, presionar para apagar").
    /// </summary>
    public HotkeyDefinition? ToggleHotkey { get; set; }

    public HotkeyDefinition ToDefinition()
    {
        if (Keys is { Length: > 0 })
        {
            return new HotkeyDefinition(Keys);
        }
        return new HotkeyDefinition(Modifiers, VirtualKey);
    }

    public void Apply(HotkeyDefinition definition)
    {
        Modifiers = definition.Modifiers;
        VirtualKey = definition.VirtualKey;
        Keys = definition.Keys is { Length: > 0 } ? definition.Keys : null;
    }
}

public sealed class AudioSettings
{
    /// <summary>WASAPI device id; null means the system default capture device.</summary>
    public string? InputDeviceId { get; set; }

    public int MaxRecordingSeconds { get; set; } = 120;

    /// <summary>Recordings shorter than this are discarded as accidental key presses.</summary>
    public int MinRecordingMilliseconds { get; set; } = 400;
}

public sealed class SttSettings
{
    /// <summary>Empty means the default location under %LOCALAPPDATA%\VoiceFlow\models.</summary>
    public string? ModelDirectory { get; set; }

    public int NumThreads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>OpenRouter Cloud STT by default as requested.</summary>
    public SttProvider Provider { get; set; } = SttProvider.OpenRouterCloud;

    /// <summary>Default model in OpenRouter Cloud STT: microsoft/mai-transcribe-2.</summary>
    public string CloudModel { get; set; } = CloudSttCatalog.DefaultModelId;

    public string ModelBaseUrl { get; set; } = ModelDownloadDefaults.BaseUrl;

    public bool VerifyHashes { get; set; } = true;
}

public sealed class LlmSettings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>DPAPI-protected blob (base64). Never stored or logged in clear text.</summary>
    public string? ProtectedApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public double Temperature { get; set; } = 0.3;

    public int MaxTokens { get; set; } = 1024;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Whether to request chain-of-thought/reasoning from models that support it.</summary>
    public bool EnableReasoning { get; set; }

    /// <summary>Reasoning effort level: "low", "medium", or "high".</summary>
    public string ReasoningEffort { get; set; } = "low";
}

public sealed class PasteSettings
{
    public PasteMode Mode { get; set; } = PasteMode.Clipboard;

    public bool RestoreClipboard { get; set; } = true;

    public int PasteDelayMilliseconds { get; set; } = 150;
}

public sealed class HistorySettings
{
    public int MaxEntries { get; set; } = 500;

    public int MaxAgeDays { get; set; } = 90;

    public bool SaveAudio { get; set; }
}

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();

    public HotkeySettings Hotkey { get; set; } = new();

    public AudioSettings Audio { get; set; } = new();

    public SttSettings Stt { get; set; } = new();

    public LlmSettings Llm { get; set; } = new();

    public PasteSettings Paste { get; set; } = new();

    public HistorySettings History { get; set; } = new();

    public List<PromptProfile> Profiles { get; set; } = BuiltInProfiles.CreateSeed();

    public string ActiveProfileId { get; set; } = "profile-cleanup";

    public PromptProfile GetActiveProfile() =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId)
        ?? Profiles.FirstOrDefault()
        ?? BuiltInProfiles.CreateSeed()[0];

    public string ResolveModelDirectory() =>
        string.IsNullOrWhiteSpace(ModelDirectoryOverride) ? AppPaths.DefaultModelDirectory : ModelDirectoryOverride!;

    private string? ModelDirectoryOverride => Stt.ModelDirectory;
}
