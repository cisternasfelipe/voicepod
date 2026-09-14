namespace VoiceFlow.Core.Models;

/// <summary>One dictation, as stored in history.db.</summary>
public sealed class HistoryEntry
{
    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int DurationMs { get; set; }

    public string RawTranscript { get; set; } = string.Empty;

    public string ProcessedText { get; set; } = string.Empty;

    public string ProfileName { get; set; } = string.Empty;

    public string ModelUsed { get; set; } = string.Empty;

    public bool PasteSucceeded { get; set; }

    public string? LlmError { get; set; }

    public int SttLatencyMs { get; set; }

    public int LlmLatencyMs { get; set; }

    /// <summary>Only set when the user opted into keeping the WAV files.</summary>
    public string? AudioPath { get; set; }
}

public sealed record HistoryQuery(
    string? SearchText = null,
    string? ProfileName = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Limit = 500,
    int Offset = 0);
