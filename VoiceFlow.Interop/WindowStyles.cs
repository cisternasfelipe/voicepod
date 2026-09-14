using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>Extended window style tweaks needed by the status overlay.</summary>
public static class WindowStyles
{
    /// <summary>
    /// Marks the window as WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW: it stays visible on top,
    /// out of Alt+Tab, and never steals focus from the window being dictated into.
    /// </summary>
    public static void MakeNonActivating(nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);
    }
}
