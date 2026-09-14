using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Storage;

/// <summary>
/// Dictation history in %LOCALAPPDATA%\VoiceFlow\history.db. Writes are small and rare, so
/// a connection per operation keeps things simple and avoids locking the file.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private const string CreateSchema = """
        CREATE TABLE IF NOT EXISTS Entries (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            CreatedAtUtc   TEXT    NOT NULL,
            DurationMs     INTEGER NOT NULL,
            RawTranscript  TEXT    NOT NULL,
            ProcessedText  TEXT    NOT NULL,
            ProfileName    TEXT    NOT NULL,
            ModelUsed      TEXT    NOT NULL,
            PasteSucceeded INTEGER NOT NULL,
            LlmError       TEXT    NULL,
            SttLatencyMs   INTEGER NOT NULL,
            LlmLatencyMs   INTEGER NOT NULL,
            AudioPath      TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Entries_CreatedAtUtc ON Entries (CreatedAtUtc DESC);
        """;

    private readonly ILogger<SqliteHistoryRepository> _logger;
    private readonly string _connectionString;

    public SqliteHistoryRepository(ILogger<SqliteHistoryRepository> logger)
        : this(logger, AppPaths.HistoryDatabase)
    {
    }

    public SqliteHistoryRepository(ILogger<SqliteHistoryRepository> logger, string databasePath)
    {
        _logger = logger;
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(CreateSchema).ConfigureAwait(false);
        _logger.LogInformation("History database ready at {Path}", DatabasePath);
    }

    public async Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO Entries (
                CreatedAtUtc, DurationMs, RawTranscript, ProcessedText, ProfileName, ModelUsed,
                PasteSucceeded, LlmError, SttLatencyMs, LlmLatencyMs, AudioPath)
            VALUES (
                @CreatedAtUtc, @DurationMs, @RawTranscript, @ProcessedText, @ProfileName, @ModelUsed,
                @PasteSucceeded, @LlmError, @SttLatencyMs, @LlmLatencyMs, @AudioPath);
            SELECT last_insert_rowid();
            """;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var id = await connection.ExecuteScalarAsync<long>(sql, ToParameters(entry)).ConfigureAwait(false);
        entry.Id = id;
        return id;
    }

    public async Task UpdateAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE Entries SET
                ProcessedText  = @ProcessedText,
                ProfileName    = @ProfileName,
                ModelUsed      = @ModelUsed,
                PasteSucceeded = @PasteSucceeded,
                LlmError       = @LlmError,
                LlmLatencyMs   = @LlmLatencyMs
            WHERE Id = @Id;
            """;

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql, new
        {
            entry.Id,
            entry.ProcessedText,
            entry.ProfileName,
            entry.ModelUsed,
            PasteSucceeded = entry.PasteSucceeded ? 1 : 0,
            entry.LlmError,
            entry.LlmLatencyMs
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder("SELECT * FROM Entries WHERE 1 = 1");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            sql.Append(" AND (RawTranscript LIKE @Search OR ProcessedText LIKE @Search)");
            parameters.Add("Search", "%" + query.SearchText.Trim() + "%");
        }

        if (!string.IsNullOrWhiteSpace(query.ProfileName))
        {
            sql.Append(" AND ProfileName = @ProfileName");
            parameters.Add("ProfileName", query.ProfileName);
        }

        if (query.FromUtc is not null)
        {
            sql.Append(" AND CreatedAtUtc >= @FromUtc");
            parameters.Add("FromUtc", Format(query.FromUtc.Value));
        }

        if (query.ToUtc is not null)
        {
            sql.Append(" AND CreatedAtUtc <= @ToUtc");
            parameters.Add("ToUtc", Format(query.ToUtc.Value));
        }

        sql.Append(" ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @Limit OFFSET @Offset");
        parameters.Add("Limit", query.Limit);
        parameters.Add("Offset", query.Offset);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<HistoryRow>(sql.ToString(), parameters).ConfigureAwait(false);
        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var names = await connection
            .QueryAsync<string>("SELECT DISTINCT ProfileName FROM Entries ORDER BY ProfileName")
            .ConfigureAwait(false);

        return names.ToList();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("DELETE FROM Entries WHERE Id = @id", new { id }).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("DELETE FROM Entries").ConfigureAwait(false);
        _logger.LogInformation("History cleared");
    }

    public async Task ApplyRetentionAsync(HistorySettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        if (settings.MaxAgeDays > 0)
        {
            var cutoff = Format(DateTime.UtcNow.AddDays(-settings.MaxAgeDays));
            await connection
                .ExecuteAsync("DELETE FROM Entries WHERE CreatedAtUtc < @cutoff", new { cutoff })
                .ConfigureAwait(false);
        }

        if (settings.MaxEntries > 0)
        {
            const string sql = """
                DELETE FROM Entries
                WHERE Id NOT IN (
                    SELECT Id FROM Entries ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @keep
                );
                """;

            await connection.ExecuteAsync(sql, new { keep = settings.MaxEntries }).ConfigureAwait(false);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static object ToParameters(HistoryEntry entry) => new
    {
        CreatedAtUtc = Format(entry.CreatedAtUtc),
        entry.DurationMs,
        entry.RawTranscript,
        entry.ProcessedText,
        entry.ProfileName,
        entry.ModelUsed,
        PasteSucceeded = entry.PasteSucceeded ? 1 : 0,
        entry.LlmError,
        entry.SttLatencyMs,
        entry.LlmLatencyMs,
        entry.AudioPath
    };

    /// <summary>Sortable, culture-independent timestamps, which is what the LIKE/range filters rely on.</summary>
    private static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

    /// <summary>Row shape as stored; keeps the DateTime parsing in one place.</summary>
    private sealed class HistoryRow
    {
        public long Id { get; set; }

        public string CreatedAtUtc { get; set; } = string.Empty;

        public int DurationMs { get; set; }

        public string RawTranscript { get; set; } = string.Empty;

        public string ProcessedText { get; set; } = string.Empty;

        public string ProfileName { get; set; } = string.Empty;

        public string ModelUsed { get; set; } = string.Empty;

        public long PasteSucceeded { get; set; }

        public string? LlmError { get; set; }

        public int SttLatencyMs { get; set; }

        public int LlmLatencyMs { get; set; }

        public string? AudioPath { get; set; }

        public HistoryEntry ToEntry() => new()
        {
            Id = Id,
            CreatedAtUtc = DateTime.TryParse(
                CreatedAtUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : DateTime.UtcNow,
            DurationMs = DurationMs,
            RawTranscript = RawTranscript,
            ProcessedText = ProcessedText,
            ProfileName = ProfileName,
            ModelUsed = ModelUsed,
            PasteSucceeded = PasteSucceeded != 0,
            LlmError = LlmError,
            SttLatencyMs = SttLatencyMs,
            LlmLatencyMs = LlmLatencyMs,
            AudioPath = AudioPath
        };
    }
}
