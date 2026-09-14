using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>
/// Delivers the final text to the window that had focus when the dictation started:
/// clipboard + Ctrl+V by default, or Unicode SendInput for apps that block pasting.
/// The previous clipboard content is restored afterwards when the user wants that.
/// </summary>
public sealed class PasteService : IPasteService
{
    private readonly IClipboardService _clipboard;
    private readonly IForegroundWindowProvider _foreground;
    private readonly ILogger<PasteService> _logger;

    public PasteService(
        IClipboardService clipboard,
        IForegroundWindowProvider foreground,
        ILogger<PasteService> logger)
    {
        _clipboard = clipboard;
        _foreground = foreground;
        _logger = logger;
    }

    public async Task<PasteResult> PasteAsync(
        string text,
        nint targetWindow,
        PasteSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new PasteResult(PasteOutcome.Failed, "No hay texto que pegar.");
        }

        if (!_foreground.IsWindow(targetWindow))
        {
            _logger.LogWarning("The target window is gone; leaving the text on the clipboard");
            var copied = await _clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);

            return copied
                ? new PasteResult(PasteOutcome.CopiedOnly, "La ventana de destino ya no existe.")
                : new PasteResult(PasteOutcome.Failed, "La ventana de destino ya no existe y el portapapeles falló.");
        }

        string? previousClipboard = null;

        if (settings.RestoreClipboard && settings.Mode == PasteMode.Clipboard)
        {
            previousClipboard = await _clipboard.GetTextAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (!FocusWindow(targetWindow))
            {
                _logger.LogWarning("SetForegroundWindow failed for {Handle}", targetWindow);
            }

            if (settings.Mode == PasteMode.TypeUnicode)
            {
                InputSender.TypeUnicode(text);
                return new PasteResult(PasteOutcome.Pasted);
            }

            if (!await _clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false))
            {
                return new PasteResult(PasteOutcome.Failed, "No se pudo escribir en el portapapeles.");
            }

            InputSender.SendCtrlV();

            // Give the target application time to read the clipboard before restoring it.
            var delay = Math.Max(settings.PasteDelayMilliseconds, 50);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            return new PasteResult(PasteOutcome.Pasted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paste failed");

            // Whatever happened, make sure the user still has the text.
            await _clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
            return new PasteResult(PasteOutcome.CopiedOnly, ex.Message);
        }
        finally
        {
            if (previousClipboard is not null)
            {
                await _clipboard.SetTextAsync(previousClipboard, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Brings the target window back to the foreground. Windows only grants this to the
    /// foreground thread, so the input queues are attached briefly to force it through.
    /// </summary>
    private static bool FocusWindow(nint target)
    {
        if (NativeMethods.IsIconic(target))
        {
            NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);
        }

        if (NativeMethods.SetForegroundWindow(target))
        {
            return true;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);

        if (targetThread == 0 || targetThread == currentThread)
        {
            return false;
        }

        NativeMethods.AttachThreadInput(currentThread, targetThread, true);

        try
        {
            return NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }
    }
}
