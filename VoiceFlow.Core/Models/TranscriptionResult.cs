namespace VoiceFlow.Core.Models;

public sealed record TranscriptionResult(string Text, int LatencyMs)
{
    public static TranscriptionResult Empty { get; } = new(string.Empty, 0);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

public sealed record LlmResult(string Text, int LatencyMs, string ModelUsed, string? Error = null)
{
    public bool Failed => Error is not null;
}

public enum SttModelStatus
{
    NotDownloaded,
    Downloading,
    Loading,
    Ready,
    Failed
}

public sealed record SttModelState(SttModelStatus Status, string? Message = null)
{
    public static SttModelState NotDownloaded { get; } = new(SttModelStatus.NotDownloaded);

    public bool IsReady => Status == SttModelStatus.Ready;
}

public sealed record ModelDownloadProgress(
    string CurrentFile,
    int FileIndex,
    int FileCount,
    long BytesReceived,
    long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp(BytesReceived / (double)TotalBytes, 0, 1);
}
