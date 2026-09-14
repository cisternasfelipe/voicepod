namespace VoiceFlow.Core;

/// <summary>
/// Central resolution of every path the application writes to, all of them under
/// %LOCALAPPDATA%\VoiceFlow so that an uninstall can decide what to keep.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "VoiceFlow";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string HistoryDatabase => Path.Combine(Root, "history.db");

    public static string ModelsDirectory => Path.Combine(Root, "models");

    public static string DefaultModelDirectory =>
        Path.Combine(ModelsDirectory, "parakeet-tdt-0.6b-v3-int8");

    public static string AudioDirectory => Path.Combine(Root, "audio");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
