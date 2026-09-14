using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Storage;

/// <summary>
/// Settings stored as JSON in %LOCALAPPDATA%\VoiceFlow\settings.json. Writes go through a
/// temporary file so a crash mid-save cannot leave a truncated configuration behind.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public JsonSettingsService(ILogger<JsonSettingsService> logger) => _logger = logger;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = AppPaths.SettingsFile;

        if (!File.Exists(path))
        {
            _logger.LogInformation("No settings file found, starting from defaults");
            Current = new AppSettings();
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            Current = Normalize(loaded ?? new AppSettings());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read settings.json, falling back to defaults");
            Current = new AppSettings();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            AppPaths.EnsureDirectory(AppPaths.Root);
            var path = AppPaths.SettingsFile;
            var temporary = path + ".tmp";

            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer
                    .SerializeAsync(stream, Current, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write settings.json");
        }
        finally
        {
            _saveLock.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    public string? GetApiKey() => DataProtector.Unprotect(Current.Llm.ProtectedApiKey);

    public void SetApiKey(string? apiKey) => Current.Llm.ProtectedApiKey = DataProtector.Protect(apiKey);

    /// <summary>Repairs anything a hand-edited settings file could have broken.</summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        settings.General ??= new GeneralSettings();
        settings.Hotkey ??= new HotkeySettings();
        settings.Audio ??= new AudioSettings();
        settings.Stt ??= new SttSettings();
        settings.Llm ??= new LlmSettings();
        settings.Paste ??= new PasteSettings();
        settings.History ??= new HistorySettings();

        if (settings.Profiles is null || settings.Profiles.Count == 0)
        {
            settings.Profiles = BuiltInProfiles.CreateSeed();
        }

        if (settings.Profiles.All(p => p.Id != settings.ActiveProfileId))
        {
            settings.ActiveProfileId = settings.Profiles[0].Id;
        }

        if (!settings.Hotkey.ToDefinition().IsValid)
        {
            settings.Hotkey.Apply(HotkeyDefinition.Default);
        }

        settings.Audio.MaxRecordingSeconds = Math.Clamp(settings.Audio.MaxRecordingSeconds, 5, 3600);
        settings.Audio.MinRecordingMilliseconds = Math.Clamp(settings.Audio.MinRecordingMilliseconds, 0, 5000);
        settings.Stt.NumThreads = Math.Clamp(settings.Stt.NumThreads, 1, 32);
        settings.Llm.TimeoutSeconds = Math.Clamp(settings.Llm.TimeoutSeconds, 5, 300);
        settings.Llm.Temperature = Math.Clamp(settings.Llm.Temperature, 0, 2);
        settings.Llm.MaxTokens = Math.Clamp(settings.Llm.MaxTokens, 16, 32_000);
        settings.Paste.PasteDelayMilliseconds = Math.Clamp(settings.Paste.PasteDelayMilliseconds, 0, 2000);
        settings.History.MaxEntries = Math.Clamp(settings.History.MaxEntries, 0, 100_000);
        settings.History.MaxAgeDays = Math.Clamp(settings.History.MaxAgeDays, 0, 3650);

        if (string.IsNullOrWhiteSpace(settings.Stt.ModelBaseUrl))
        {
            settings.Stt.ModelBaseUrl = ModelDownloadDefaults.BaseUrl;
        }

        return settings;
    }
}
