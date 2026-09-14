using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Decrypts the stored API key with DPAPI. Returns null when none is set.</summary>
    string? GetApiKey();

    /// <summary>Encrypts and stores the API key with DPAPI (CurrentUser scope).</summary>
    void SetApiKey(string? apiKey);
}

public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies the retention policy (max entries / max age).</summary>
    Task ApplyRetentionAsync(HistorySettings settings, CancellationToken cancellationToken = default);
}
