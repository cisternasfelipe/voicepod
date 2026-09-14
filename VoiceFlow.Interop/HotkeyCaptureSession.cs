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
    private readonly List<int> _maxHeldCombination = new();
    private readonly HashSet<int> _keysCurrentlyDown = new();
    private nint _hookHandle;
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

            _keysCurrentlyDown.Clear();
            _maxHeldCombination.Clear();
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

            _keysCurrentlyDown.Clear();
            _maxHeldCombination.Clear();
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

                _keysCurrentlyDown.Add(vkCode);
                if (!_maxHeldCombination.Contains(vkCode))
                {
                    _maxHeldCombination.Add(vkCode);
                }

                var previewDef = new HotkeyDefinition(_maxHeldCombination.ToArray());
                var displayText = previewDef.ToDisplayString();
                ModifiersChanged?.Invoke(this, previewDef.Modifiers);

                string hint;
                var hasNonModifier = _maxHeldCombination.Any(k => !IsModifierVk(k));
                if (_maxHeldCombination.Count >= 2 || hasNonModifier)
                {
                    hint = "🟢 ¡Suelta las teclas para guardar la combinación!";
                }
                else
                {
                    displayText += " + ...";
                    hint = "🔴 Presiona otra tecla para completar (ej: Alt, Espacio, F8)...";
                }

                PreviewUpdated?.Invoke(this, new HotkeyCapturePreview(previewDef, displayText, hint));
            }
            else // isUp
            {
                _keysCurrentlyDown.Remove(vkCode);

                if (_maxHeldCombination.Count == 0)
                {
                    return;
                }

                // If only 1 modifier key was tapped and released alone (e.g. tapped only Left Ctrl)
                if (_maxHeldCombination.Count == 1 && IsModifierVk(_maxHeldCombination[0]))
                {
                    if (_keysCurrentlyDown.Count == 0)
                    {
                        _debounceTimer?.Dispose();
                        _debounceTimer = new Timer(_ =>
                        {
                            lock (_sync)
                            {
                                if (_hookHandle == 0) return;
                                if (_keysCurrentlyDown.Count == 0 && _maxHeldCombination.Count == 1 && IsModifierVk(_maxHeldCombination[0]))
                                {
                                    _maxHeldCombination.Clear();
                                    PreviewUpdated?.Invoke(this, new HotkeyCapturePreview(
                                        new HotkeyDefinition(),
                                        "",
                                        "🔴 Un solo modificador no es válido. Presiona una combinación (ej: Ctrl + Alt o Ctrl + Alt + Ctrl + Alt)."
                                    ));
                                }
                            }
                        }, null, 700, Timeout.Infinite);
                    }
                    return;
                }

                // Valid combination (2+ keys, or single non-modifier key like F8)
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ =>
                {
                    lock (_sync)
                    {
                        if (_hookHandle == 0 || _maxHeldCombination.Count == 0) return;
                        var finalDef = new HotkeyDefinition(_maxHeldCombination.ToArray());
                        Stop();
                        HotkeyCaptured?.Invoke(this, finalDef);
                    }
                }, null, 180, Timeout.Infinite);
            }
        }
    }

    private static bool IsModifierVk(int vkCode) =>
        vkCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

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
