using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>
/// Clipboard access from a dedicated STA thread with retries: the Windows clipboard is a
/// shared, lockable resource and fails intermittently while another process holds it open.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(60);

    private readonly ILogger<ClipboardService> _logger;

    public ClipboardService(ILogger<ClipboardService> logger) => _logger = logger;

    public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        RunOnStaThread(() => SetTextCore(text), false, cancellationToken);

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        RunOnStaThread<string?>(GetTextCore, null, cancellationToken);

    private static bool SetTextCore(string text)
    {
        if (!ClipboardNative.OpenClipboard(0))
        {
            throw new InvalidOperationException("OpenClipboard failed.");
        }

        var handle = nint.Zero;

        try
        {
            ClipboardNative.EmptyClipboard();
            handle = ClipboardNative.AllocateUnicodeText(text);

            if (ClipboardNative.SetClipboardData(ClipboardNative.CF_UNICODETEXT, handle) == 0)
            {
                throw new InvalidOperationException("SetClipboardData failed.");
            }

            // Ownership of the block now belongs to the clipboard.
            handle = nint.Zero;
            return true;
        }
        finally
        {
            if (handle != nint.Zero)
            {
                ClipboardNative.GlobalFree(handle);
            }

            ClipboardNative.CloseClipboard();
        }
    }

    private static string? GetTextCore()
    {
        if (!ClipboardNative.OpenClipboard(0))
        {
            throw new InvalidOperationException("OpenClipboard failed.");
        }

        try
        {
            return ClipboardNative.ReadUnicodeText();
        }
        finally
        {
            ClipboardNative.CloseClipboard();
        }
    }

    private Task<T> RunOnStaThread<T>(Func<T> action, T fallback, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetResult(fallback);
                    return;
                }

                try
                {
                    completion.TrySetResult(action());
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Clipboard access failed (attempt {Attempt}/{Max})", attempt, MaxAttempts);

                    if (attempt == MaxAttempts)
                    {
                        _logger.LogWarning(ex, "Giving up on the clipboard after {Max} attempts", MaxAttempts);
                        completion.TrySetResult(fallback);
                        return;
                    }

                    Thread.Sleep(RetryDelay);
                }
            }
        })
        {
            IsBackground = true,
            Name = "VoiceFlow.Clipboard"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
