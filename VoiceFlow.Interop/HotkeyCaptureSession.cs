using System.Runtime.InteropServices;
using VoiceFlow.Core.Models;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

public sealed record HotkeyCapturePreview(
    HotkeyDefinition Definition,
    string DisplayText,
    string HintText
);

/// <summary>
/// A temporary low-level keyboard hook session used by the UI to capture
/// any user keystroke (including pure modifier combinations like Ctrl + Alt,
/// single keys like F8, or standard combinations).
/// </summary>
public sealed class HotkeyCaptureSession : IDisposable
{
    private readonly HookProc _hookProc;
    private readonly object _sync = new();
    private readonly HashSet<int> _keysDown = new();

    private nint _hookHandle;
    private HotkeyModifiers _accumulatedModifiers = HotkeyModifiers.None;
    private int _accumulatedKey;
    private Timer? _debounceTimer;
    private bool _disposed;

    public HotkeyCaptureSession()
    {
        _hookProc = HookCallback;
    }

    public bool IsActive => _hookHandle != 0;

    public event EventHandler<HotkeyDefinition>? HotkeyCaptured;
    public event EventHandler<HotkeyCapturePreview>? PreviewUpdated;
    public event EventHandler<HotkeyModifiers>? ModifiersChanged;
    public event EventHandler? Cancelled;

    public void Start()
    {
        lock (_sync)
        {
            if (_hookHandle != 0)
            {
                return;
            }

            _keysDown.Clear();
            _accumulatedModifiers = HotkeyModifiers.None;
            _accumulatedKey = 0;
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            var hModule = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, hModule, 0);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (_hookHandle != 0)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = 0;
            }

            _keysDown.Clear();
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _hookHandle != 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var msg = (uint)wParam;
            var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                HandleKeyEvent(vkCode, isDown);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void HandleKeyEvent(int vkCode, bool isDown)
    {
        lock (_sync)
        {
            if (_hookHandle == 0)
            {
                return;
            }

            if (isDown)
            {
                // Escape cancels recording
                if (vkCode == 0x1B)
                {
                    Stop();
                    Cancelled?.Invoke(this, EventArgs.Empty);
                    return;
                }

                _debounceTimer?.Dispose();
                _debounceTimer = null;
                _keysDown.Add(vkCode);

                if (IsModifierVk(vkCode, out var modifier))
                {
                    _accumulatedModifiers |= modifier;
                    ModifiersChanged?.Invoke(this, _accumulatedModifiers);

                    var count = CountModifiers(_accumulatedModifiers);
                    var previewDef = new HotkeyDefinition(_accumulatedModifiers, 0);
                    var displayText = previewDef.ToDisplayString();

                    string hint;
                    if (count >= 2)
                    {
                        hint = "🟢 ¡Suelta las teclas para guardar!";
                    }
                    else
                    {
                        displayText += " + ...";
                        hint = "🔴 Presiona otra tecla para completar (ej: Alt, Espacio, F8)...";
                    }

                    PreviewUpdated?.Invoke(this, new HotkeyCapturePreview(previewDef, displayText, hint));
                }
                else
                {
                    // Regular key pressed (e.g. Space, F8, A, Enter, etc.)
                    _accumulatedKey = vkCode;
                    var definition = new HotkeyDefinition(_accumulatedModifiers, vkCode);
                    Stop();
                    HotkeyCaptured?.Invoke(this, definition);
                }
            }
            else // isUp
            {
                _keysDown.Remove(vkCode);

                // If a regular key was already captured on isDown, nothing to do
                if (_accumulatedKey != 0)
                {
                    return;
                }

                var modCount = CountModifiers(_accumulatedModifiers);
                if (modCount >= 2)
                {
                    // If all keys are released (or after tiny debounce), finalize pure modifiers (e.g. Ctrl + Alt)
                    if (_keysDown.Count == 0)
                    {
                        _debounceTimer?.Dispose();
                        _debounceTimer = new Timer(_ =>
                        {
                            lock (_sync)
                            {
                                if (_hookHandle == 0) return;
                                var def = new HotkeyDefinition(_accumulatedModifiers, 0);
                                Stop();
                                HotkeyCaptured?.Invoke(this, def);
                            }
                        }, null, 250, Timeout.Infinite);
                    }
                }
                else if (modCount == 1 && _keysDown.Count == 0)
                {
                    // A single modifier was released alone (e.g. user pressed and released just Ctrl)
                    // Wait up to 1000ms for a second key; if nothing pressed, reset
                    _debounceTimer?.Dispose();
                    _debounceTimer = new Timer(_ =>
                    {
                        lock (_sync)
                        {
                            if (_hookHandle == 0) return;
                            if (_keysDown.Count == 0 && CountModifiers(_accumulatedModifiers) == 1)
                            {
                                _accumulatedModifiers = HotkeyModifiers.None;
                                PreviewUpdated?.Invoke(this, new HotkeyCapturePreview(
                                    new HotkeyDefinition(HotkeyModifiers.None, 0),
                                    "",
                                    "🔴 Un solo modificador no es válido. Presiona una combinación (ej: Ctrl + Alt)."
                                ));
                            }
                        }
                    }, null, 1000, Timeout.Infinite);
                }
            }
        }
    }

    private static bool IsModifierVk(int vkCode, out HotkeyModifiers modifier)
    {
        switch (vkCode)
        {
            case 0x11: // VK_CONTROL
            case 0xA2: // VK_LCONTROL
            case 0xA3: // VK_RCONTROL
                modifier = HotkeyModifiers.Control;
                return true;

            case 0x12: // VK_MENU (Alt)
            case 0xA4: // VK_LMENU
            case 0xA5: // VK_RMENU
                modifier = HotkeyModifiers.Alt;
                return true;

            case 0x10: // VK_SHIFT
            case 0xA0: // VK_LSHIFT
            case 0xA1: // VK_RSHIFT
                modifier = HotkeyModifiers.Shift;
                return true;

            case 0x5B: // VK_LWIN
            case 0x5C: // VK_RWIN
                modifier = HotkeyModifiers.Win;
                return true;

            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static int CountModifiers(HotkeyModifiers modifiers)
    {
        var count = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) count++;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) count++;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) count++;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) count++;
        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
