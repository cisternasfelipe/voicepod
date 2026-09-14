using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace VoiceFlow.App.Services;

/// <summary>
/// "Iniciar con Windows" through the per-user Run key. The entry starts VoiceFlow
/// straight into the tray, so a reboot leaves the app ready without opening a window.
/// </summary>
public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceFlow";

    private readonly ILogger<StartupRegistration> _logger;

    public StartupRegistration(ILogger<StartupRegistration> logger) => _logger = logger;

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Run key");
            return false;
        }
    }

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Startup entry removed");
                return;
            }

            key.SetValue(ValueName, $"\"{GetExecutablePath()}\" {App.TrayStartupSwitch}");
            _logger.LogInformation("Startup entry written");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update the Run key");
        }
    }

    private static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Process.GetCurrentProcess().MainModule?.FileName ?? "VoiceFlow.exe";
    }
}
