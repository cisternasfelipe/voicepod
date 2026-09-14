using VoiceFlow.Core.Abstractions;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

public sealed class ForegroundWindowProvider : IForegroundWindowProvider
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public bool IsWindow(nint handle) => handle != 0 && NativeMethods.IsWindow(handle);

    public string GetWindowTitle(nint handle)
    {
        if (!IsWindow(handle))
        {
            return string.Empty;
        }

        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }
}
