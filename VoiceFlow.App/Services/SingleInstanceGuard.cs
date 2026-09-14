namespace VoiceFlow.App.Services;

/// <summary>
/// Keeps a single VoiceFlow process per Windows session. A second launch signals the
/// running one to show its window and then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\VoiceFlow.SingleInstance";
    private const string SignalName = @"Local\VoiceFlow.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listener;
    private bool _disposed;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    private const uint ASFW_ANY = unchecked((uint)-1);

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread when another instance asks for the window.</summary>
    public event EventHandler? ShowRequested;

    public void SignalExistingInstance()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }
        catch
        {
            // Ignore if OS does not allow
        }

        _signal.Set();
    }

    public void StartListening()
    {
        if (!IsFirstInstance || _listener is not null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _signal, _cancellation.Token.WaitHandle };

            while (!_cancellation.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) == 0)
                {
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        })
        {
            IsBackground = true,
            Name = "VoiceFlow.SingleInstanceListener"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _listener?.Join(TimeSpan.FromSeconds(1));

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released during an abrupt shutdown.
            }
        }

        _mutex.Dispose();
        _signal.Dispose();
        _cancellation.Dispose();
    }
}
