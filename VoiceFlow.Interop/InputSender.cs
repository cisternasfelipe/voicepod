using System.Runtime.InteropServices;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>Synthesised keyboard input through SendInput.</summary>
public static class InputSender
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>Sends Ctrl+V to the focused window.</summary>
    public static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_CONTROL)
        };

        Send(inputs);
    }

    /// <summary>
    /// Types the text character by character as Unicode input, for applications that
    /// ignore or block clipboard pastes.
    /// </summary>
    public static void TypeUnicode(string text)
    {
        foreach (var character in text)
        {
            if (character == '\n')
            {
                // Enter, so line breaks survive in editors that expect a real key press.
                Send([KeyDown(0x0D), KeyUp(0x0D)]);
                continue;
            }

            if (character == '\r')
            {
                continue;
            }

            Send([UnicodeDown(character), UnicodeUp(character)]);
        }
    }

    private static void Send(INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);

        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput delivered {sent} of {inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
        }
    }

    private static INPUT KeyDown(int virtualKey) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)virtualKey } }
    };

    private static INPUT KeyUp(int virtualKey) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = (ushort)virtualKey, dwFlags = NativeMethods.KEYEVENTF_KEYUP }
        }
    };

    private static INPUT UnicodeDown(char character) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wScan = character, dwFlags = NativeMethods.KEYEVENTF_UNICODE }
        }
    };

    private static INPUT UnicodeUp(char character) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
            }
        }
    };
}
