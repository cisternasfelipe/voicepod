using System.ComponentModel;
using System.Windows;
using VoiceFlow.App.ViewModels;
using VoiceFlow.Core.Abstractions;

namespace VoiceFlow.App.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => UpdateMaximizeButtonIcon();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeButtonIcon();
    }

    private void UpdateMaximizeButtonIcon()
    {
        if (MaximizeIcon is not null)
        {
            MaximizeIcon.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
        }
    }

    /// <summary>Set by the host when the user really wants the process to end.</summary>
    public bool ForceClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ForceClose && _settings.Current.General.MinimizeToTrayOnClose)
        {
            // The X button sends the app to the tray; the hotkey keeps working.
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public void ShowAndActivate()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
        }
        catch
        {
            // Ignore if helper fails
        }
    }
}
