using VoiceFlow.Interop.Native;

namespace VoiceFlow.Interop;

/// <summary>
/// A hidden message-only window living on its own thread with its own message loop.
/// It exists so global hotkeys keep working while the main window is minimised,
/// hidden or closed to the tray.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProcDelegate _wndProc;      // kept alive: the OS holds a raw pointer to it
    private readonly Thread _thread;
    private readonly string _className;

    private nint _handle;
    private uint _threadId;
    private bool _disposed;

    public MessageWindow(Func<uint, nint, nint, bool> messageHandler)
    {
        MessageHandler = messageHandler;
        _wndProc = WndProc;
        _className = "VoiceFlowMessageWindow_" + Guid.NewGuid().ToString("N");

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "VoiceFlow.MessageWindow"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("No se pudo crear la ventana oculta de mensajes.");
        }
    }

    /// <summary>Returns true when the message was handled and must not reach DefWindowProc.</summary>
    private Func<uint, nint, nint, bool> MessageHandler { get; }

    public nint Handle => _handle;

    /// <summary>Runs <paramref name="action"/> on the message window thread and waits for it.</summary>
    public T Invoke<T>(Func<T> action)
    {
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            return action();
        }

        T result = default!;
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);

        _pending.Enqueue(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });

        NativeMethods.PostMessage(_handle, WmRunPending, 0, 0);
        done.Wait();

        if (failure is not null)
        {
            throw failure;
        }

        return result;
    }

    public void Invoke(Action action) => Invoke(() =>
    {
        action();
        return true;
    });

    private const uint WmRunPending = 0x8000 + 1;

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending = new();

    private void RunMessageLoop()
    {
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = moduleHandle,
            lpszClassName = _className
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            _ready.Set();
            return;
        }

        _handle = NativeMethods.CreateWindowEx(
            0, _className, "VoiceFlow", 0, 0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE, 0, moduleHandle, 0);

        _threadId = NativeMethods.GetCurrentThreadId();
        _ready.Set();

        if (_handle == 0)
        {
            return;
        }

        while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmRunPending:
                while (_pending.TryDequeue(out var action))
                {
                    action();
                }

                return 0;

            case NativeMethods.WM_APP_QUIT:
                NativeMethods.DestroyWindow(hWnd);
                return 0;

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return 0;
        }

        return MessageHandler(msg, wParam, lParam)
            ? 0
            : NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != 0)
        {
            NativeMethods.PostMessage(_handle, NativeMethods.WM_APP_QUIT, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(2));
            _handle = 0;
        }

        _ready.Dispose();
    }
}
