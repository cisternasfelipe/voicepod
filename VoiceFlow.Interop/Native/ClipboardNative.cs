using System.Runtime.InteropServices;

namespace VoiceFlow.Interop.Native;

/// <summary>Raw Win32 clipboard access, so the interop layer stays free of WPF.</summary>
internal static class ClipboardNative
{
    public const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalFree(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nuint GlobalSize(nint hMem);

    /// <summary>Copies a UTF-16 string into a moveable global block the clipboard can own.</summary>
    public static nint AllocateUnicodeText(string text)
    {
        var bytes = (nuint)((text.Length + 1) * sizeof(char));
        var handle = GlobalAlloc(GMEM_MOVEABLE, bytes);

        if (handle == 0)
        {
            throw new InvalidOperationException("GlobalAlloc failed for the clipboard buffer.");
        }

        var pointer = GlobalLock(handle);

        if (pointer == 0)
        {
            GlobalFree(handle);
            throw new InvalidOperationException("GlobalLock failed for the clipboard buffer.");
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    public static string? ReadUnicodeText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
        {
            return null;
        }

        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == 0)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }
}
