using System.IO;
using VoiceFlow.Core;

namespace VoiceFlow.App.Services;

/// <summary>
/// Last-resort logger for failures that happen before (or instead of) the regular logging
/// pipeline. Writes plain text to %LOCALAPPDATA%\VoiceFlow\logs\startup.log.
/// </summary>
public static class BootstrapLog
{
    private static readonly object Sync = new();

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            var path = Path.Combine(AppPaths.EnsureDirectory(AppPaths.LogsDirectory), "startup.log");
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}"
                       + (exception is null ? string.Empty : Environment.NewLine + exception)
                       + Environment.NewLine;

            lock (Sync)
            {
                File.AppendAllText(path, text);
            }
        }
        catch
        {
            // Nothing sensible to do if even this fails.
        }
    }
}
