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

    private HotkeyMode _mode = HotkeyMode.Toggle;
    private nint _hookHandle;
    private bool _isDown;
    private bool _disposed;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
        _hookProc = LowLevelKeyboardCallback;
        _window = new MessageWindow((_, _, _) => false);
    }

    public HotkeyDefinition? Current { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyRegistrationResult Register(HotkeyDefinition definition, HotkeyMode mode)
    {
        if (!definition.IsValid)
        {
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
            Current = definition;
            _mode = mode;
            _isDown = false;
            _logger.LogInformation("Low-level hotkey {Hotkey} registered in {Mode} mode", definition.ToDisplayString(), mode);
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
        Current = null;
        _isDown = false;
    }

    private nint LowLevelKeyboardCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && Current is not null)
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
        var target = Current;
        if (target is null)
        {
            return;
        }

        var isUpMsg = !isDownMsg;

        var ctrl = IsKeyCurrentlyDown(NativeMethods.VK_CONTROL) || (isDownMsg && vkCode is 0x11 or 0xA2 or 0xA3);
        if (!isDownMsg && vkCode is 0x11 or 0xA2 or 0xA3) ctrl = false;

        var alt = IsKeyCurrentlyDown(NativeMethods.VK_MENU) || (isDownMsg && vkCode is 0x12 or 0xA4 or 0xA5);
        if (!isDownMsg && vkCode is 0x12 or 0xA4 or 0xA5) alt = false;

        var shift = IsKeyCurrentlyDown(NativeMethods.VK_SHIFT) || (isDownMsg && vkCode is 0x10 or 0xA0 or 0xA1);
        if (!isDownMsg && vkCode is 0x10 or 0xA0 or 0xA1) shift = false;

        var win = IsKeyCurrentlyDown(NativeMethods.VK_LWIN) || IsKeyCurrentlyDown(0x5C) || (isDownMsg && vkCode is 0x5B or 0x5C);
        if (!isDownMsg && vkCode is 0x5B or 0x5C) win = false;

        var requiredModifiers = target.Modifiers;
        var modifiersMatch = (requiredModifiers.HasFlag(HotkeyModifiers.Control) == ctrl)
                             && (requiredModifiers.HasFlag(HotkeyModifiers.Alt) == alt)
                             && (requiredModifiers.HasFlag(HotkeyModifiers.Shift) == shift)
                             && (requiredModifiers.HasFlag(HotkeyModifiers.Win) == win);

        if (target.VirtualKey == 0)
        {
            // Pure modifier shortcut (e.g. Ctrl + Alt)
            var allPressed = modifiersMatch && (requiredModifiers != HotkeyModifiers.None);
            if (isDownMsg && allPressed)
            {
                if (!_isDown)
                {
                    _isDown = true;
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (isUpMsg && (!modifiersMatch || !allPressed))
            {
                if (_isDown)
                {
                    _isDown = false;
                    if (_mode == HotkeyMode.PushToTalk)
                    {
                        Released?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
        else
        {
            // Key combination (with or without modifiers)
            var isTargetKey = vkCode == target.VirtualKey;

            if (isDownMsg && isTargetKey && modifiersMatch)
            {
                if (!_isDown)
                {
                    _isDown = true;
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (isUpMsg && (isTargetKey || !modifiersMatch))
            {
                if (_isDown)
                {
                    _isDown = false;
                    if (_mode == HotkeyMode.PushToTalk)
                    {
                        Released?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
    }

    private static bool IsKeyCurrentlyDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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
