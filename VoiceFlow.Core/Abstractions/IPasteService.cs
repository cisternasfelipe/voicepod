using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

public enum PasteOutcome
{
    Pasted,
    CopiedOnly,
    Failed
}

public sealed record PasteResult(PasteOutcome Outcome, string? Error = null)
{
    public bool Succeeded => Outcome == PasteOutcome.Pasted;
}

public interface IPasteService
{
    /// <summary>
    /// Restores focus to <paramref name="targetWindow"/> and delivers the text there.
    /// Falls back to leaving the text on the clipboard when the window is gone.
    /// </summary>
    Task<PasteResult> PasteAsync(string text, nint targetWindow, PasteSettings settings, CancellationToken cancellationToken = default);
}

public interface IClipboardService
{
    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}

public interface IForegroundWindowProvider
{
    /// <summary>HWND of the window that had focus when the dictation started.</summary>
    nint GetForegroundWindow();

    bool IsWindow(nint handle);

    string GetWindowTitle(nint handle);
}
