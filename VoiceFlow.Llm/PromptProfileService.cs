using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Llm;

/// <summary>
/// CRUD over the prompt profiles stored in settings.json, plus the active-profile switch
/// used by the tray menu. Every change is persisted immediately so the next dictation
/// picks it up without restarting the app.
/// </summary>
public sealed class PromptProfileService
{
    private readonly ISettingsService _settings;

    public PromptProfileService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<PromptProfile> Profiles => _settings.Current.Profiles;

    public PromptProfile Active => _settings.Current.GetActiveProfile();

    public event EventHandler? ProfilesChanged;

    public async Task<PromptProfile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var profile = new PromptProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Nuevo perfil" : name.Trim(),
            SystemPrompt = string.Empty
        };

        _settings.Current.Profiles.Add(profile);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<PromptProfile> DuplicateAsync(string id, CancellationToken cancellationToken = default)
    {
        var source = Find(id) ?? throw new InvalidOperationException("El perfil no existe.");
        var copy = source.Clone();

        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " (copia)";
        copy.BuiltInKey = null;

        _settings.Current.Profiles.Add(copy);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return copy;
    }

    public async Task UpdateAsync(PromptProfile profile, CancellationToken cancellationToken = default)
    {
        var stored = Find(profile.Id) ?? throw new InvalidOperationException("El perfil no existe.");

        stored.Name = profile.Name;
        stored.SystemPrompt = profile.SystemPrompt;
        stored.Model = profile.Model;
        stored.Temperature = profile.Temperature;
        stored.UsesLlm = profile.UsesLlm;

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var profiles = _settings.Current.Profiles;

        if (profiles.Count <= 1)
        {
            throw new InvalidOperationException("Debe quedar al menos un perfil.");
        }

        var stored = Find(id);
        if (stored is null)
        {
            return;
        }

        profiles.Remove(stored);

        if (_settings.Current.ActiveProfileId == id)
        {
            _settings.Current.ActiveProfileId = profiles[0].Id;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetActiveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (Find(id) is null)
        {
            return;
        }

        _settings.Current.ActiveProfileId = id;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Prompt a built-in profile shipped with, so the editor can offer a reset.</summary>
    public static string? GetDefaultPrompt(PromptProfile profile) =>
        profile.BuiltInKey is not null
        && BuiltInProfiles.DefaultPrompts.TryGetValue(profile.BuiltInKey, out var prompt)
            ? prompt
            : null;

    public PromptProfile? Find(string id) => _settings.Current.Profiles.FirstOrDefault(p => p.Id == id);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
}
