using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>
/// Global hotkey based on a low-level keyboard hook (WH_KEYBOARD_LL).
/// Works for modifier-only shortcuts (like Ctrl + Alt), single-key shortcuts (like F8),
/// and standard combinations, without colliding with RegisterHotKey or layout issues.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly MessageWindow _window;
    private readonly object _sync = new();
    private readonly HookProc _hookProc;
    private readonly HashSet<int> _downKeys = new();

    private nint _hookHandle;
    private bool _isHoldDown;
    private bool _isToggleDown;
    private bool _disposed;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
        _hookProc = LowLevelKeyboardCallback;
        _window = new MessageWindow((_, _, _) => false);
    }

    public HotkeyDefinition? Current => HoldHotkey ?? ToggleHotkey;

    public HotkeyDefinition? HoldHotkey { get; private set; }

    public HotkeyDefinition? ToggleHotkey { get; private set; }

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? HoldPressed;
    public event EventHandler? HoldReleased;
    public event EventHandler? TogglePressed;

    public HotkeyRegistrationResult Register(HotkeyDefinition definition, HotkeyMode mode)
    {
        if (mode == HotkeyMode.PushToTalk)
        {
            return RegisterDual(definition, null);
        }
        else
        {
            return RegisterDual(null, definition);
        }
    }

    public HotkeyRegistrationResult RegisterDual(HotkeyDefinition? holdDefinition, HotkeyDefinition? toggleDefinition)
    {
        var hasHold = holdDefinition is not null && holdDefinition.IsValid;
        var hasToggle = toggleDefinition is not null && toggleDefinition.IsValid;

        if (!hasHold && !hasToggle)
        {
            Unregister();
            return HotkeyRegistrationResult.Fail(HotkeyFailure.InvalidCombination);
        }

        lock (_sync)
        {
            UnregisterCore();

            var (success, errorCode, hookHandle) = _window.Invoke(() =>
            {
                var hModule = NativeMethods.GetModuleHandle(null);
                var hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, hModule, 0);
                var err = hook == 0 ? Marshal.GetLastWin32Error() : 0;
                return (hook != 0, err, hook);
            });

            if (!success)
            {
                var detail = new Win32Exception(errorCode).Message;
                _logger.LogWarning("Low-level keyboard hook installation failed ({Code}): {Message}", errorCode, detail);
                return HotkeyRegistrationResult.Fail(HotkeyFailure.Other, detail);
            }

            _hookHandle = hookHandle;
            HoldHotkey = hasHold ? holdDefinition : null;
            ToggleHotkey = hasToggle ? toggleDefinition : null;
            _isHoldDown = false;
            _isToggleDown = false;
            _downKeys.Clear();

            _logger.LogInformation(
                "Global hotkeys registered -> Hold: {Hold}, Toggle: {Toggle}",
                HoldHotkey?.ToDisplayString() ?? "None",
                ToggleHotkey?.ToDisplayString() ?? "None");

            return HotkeyRegistrationResult.Ok();
        }
    }

    public void Unregister()
    {
        lock (_sync)
        {
            UnregisterCore();
        }
    }

    private void UnregisterCore()
    {
        if (_hookHandle == 0)
        {
            return;
        }

        var handle = _hookHandle;
        _hookHandle = 0;
        _window.Invoke(() => NativeMethods.UnhookWindowsHookEx(handle));
        HoldHotkey = null;
        ToggleHotkey = null;
        _isHoldDown = false;
        _isToggleDown = false;
        _downKeys.Clear();
    }

    private nint LowLevelKeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _hookHandle != 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var msg = (uint)wParam;
            var isDownMsg = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var isUpMsg = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isDownMsg || isUpMsg)
            {
                ProcessKeyEvent(vkCode, isDownMsg);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ProcessKeyEvent(int vkCode, bool isDownMsg)
    {
        lock (_sync)
        {
            if (isDownMsg)
            {
                _downKeys.Add(vkCode);
            }
            else
            {
                _downKeys.Remove(vkCode);
            }

            // Check Hold (Push to Talk)
            if (HoldHotkey is not null && HoldHotkey.IsValid)
            {
                var allHoldDown = AreAllKeysDown(HoldHotkey, vkCode, isDownMsg);
                if (allHoldDown)
                {
                    if (!_isHoldDown)
                    {
                        _isHoldDown = true;
                        _logger.LogDebug("Hold hotkey triggered ({Hotkey})", HoldHotkey.ToDisplayString());
                        HoldPressed?.Invoke(this, EventArgs.Empty);
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                }
                else
                {
                    if (_isHoldDown)
                    {
                        _isHoldDown = false;
                        _logger.LogDebug("Hold hotkey released ({Hotkey})", HoldHotkey.ToDisplayString());
                        HoldReleased?.Invoke(this, EventArgs.Empty);
                        Released?.Invoke(this, EventArgs.Empty);
                    }
                }
            }

            // Check Toggle (Pulsar para activar / apagar)
            if (ToggleHotkey is not null && ToggleHotkey.IsValid)
            {
                var allToggleDown = AreAllKeysDown(ToggleHotkey, vkCode, isDownMsg);
                if (allToggleDown)
                {
                    if (!_isToggleDown)
                    {
                        _isToggleDown = true;
                        _logger.LogDebug("Toggle hotkey pressed ({Hotkey})", ToggleHotkey.ToDisplayString());
                        TogglePressed?.Invoke(this, EventArgs.Empty);
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                }
                else
                {
                    _isToggleDown = false;
                }
            }
        }
    }

    private bool AreAllKeysDown(HotkeyDefinition hotkey, int triggeringVk, bool isDownMsg)
    {
        var keys = hotkey.Keys;
        if (keys.Length == 0)
        {
            return false;
        }

        foreach (var key in keys)
        {
            if (!IsKeyHeld(key, triggeringVk, isDownMsg))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsKeyHeld(int key, int triggeringVk, bool isDownMsg)
    {
        if (key == triggeringVk)
        {
            return isDownMsg;
        }

        // Generic modifier check if key is generic
        if (key == 0x11) // VK_CONTROL
        {
            if (triggeringVk is 0xA2 or 0xA3) return isDownMsg;
            return (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
        }
        if (key == 0x12) // VK_MENU (Alt)
        {
            if (triggeringVk is 0xA4 or 0xA5) return isDownMsg;
            return (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
        }
        if (key == 0x10) // VK_SHIFT
        {
            if (triggeringVk is 0xA0 or 0xA1) return isDownMsg;
            return (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
        }
        if (key is 0x5B or 0x5C) // VK_WIN
        {
            if (triggeringVk is 0x5B or 0x5C) return isDownMsg;
            return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        }

        // Specific keys (e.g. 0xA2 LControl, 0xA4 LAlt, 0xA3 RControl, 0xA5 RAlt, letters, numbers, Space, etc.)
        if (_downKeys.Contains(key))
        {
            return true;
        }

        return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _window.Dispose();
    }
}
