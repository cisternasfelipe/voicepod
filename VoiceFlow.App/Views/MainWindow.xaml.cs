using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using VoiceFlow.App.ViewModels;
using VoiceFlow.Core.Abstractions;

namespace VoiceFlow.App.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;

    private const double MinZoom = 0.70;
    private const double MaxZoom = 1.60;
    private const double ZoomStep = 0.10;
    private double _currentZoom = 1.0;
    private double _baseWidth = 750;
    private double _baseHeight = 570;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        _baseWidth = Width;
        _baseHeight = Height;

        Loaded += (_, _) => UpdateMaximizeButtonIcon();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDwmRoundedCorners();
    }

    #region Window Controls

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
            MaximizeIcon.Kind = WindowState == WindowState.Maximized
                ? MahApps.Metro.IconPacks.PackIconLucideKind.Copy
                : MahApps.Metro.IconPacks.PackIconLucideKind.Square;
        }

        if (WindowBorder is not null)
        {
            WindowBorder.CornerRadius = WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(14);

            WindowBorder.BorderThickness = WindowState == WindowState.Maximized
                ? new Thickness(0)
                : new Thickness(2.3);
        }
    }

    #endregion

    #region Zoom Functionality (Ctrl + / Ctrl - / Ctrl Wheel)

    public void SetZoom(double targetZoom)
    {
        targetZoom = Math.Round(Math.Clamp(targetZoom, MinZoom, MaxZoom), 2);
        if (Math.Abs(_currentZoom - targetZoom) < 0.005) return;

        _currentZoom = targetZoom;

        if (ContentScaleTransform is not null)
        {
            ContentScaleTransform.ScaleX = _currentZoom;
            ContentScaleTransform.ScaleY = _currentZoom;
        }

        if (ZoomPercentText is not null)
        {
            ZoomPercentText.Text = $"{(int)Math.Round(_currentZoom * 100)}%";
        }

        // Proportional window resize if in normal window state
        if (WindowState == WindowState.Normal)
        {
            var workArea = SystemParameters.WorkArea;
            double targetWidth = Math.Min(workArea.Width * 0.96, Math.Max(MinWidth, _baseWidth * _currentZoom));
            double targetHeight = Math.Min(workArea.Height * 0.96, Math.Max(MinHeight, _baseHeight * _currentZoom));

            double centerX = Left + Width / 2.0;
            double centerY = Top + Height / 2.0;

            Width = targetWidth;
            Height = targetHeight;

            double newLeft = centerX - targetWidth / 2.0;
            double newTop = centerY - targetHeight / 2.0;

            Left = Math.Max(workArea.Left, Math.Min(workArea.Right - targetWidth, newLeft));
            Top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - targetHeight, newTop));
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                SetZoom(_currentZoom + ZoomStep);
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                SetZoom(_currentZoom - ZoomStep);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                SetZoom(1.0);
                e.Handled = true;
            }
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0)
            {
                SetZoom(_currentZoom + ZoomStep);
            }
            else if (e.Delta < 0)
            {
                SetZoom(_currentZoom - ZoomStep);
            }
            e.Handled = true;
        }
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) =>
        SetZoom(_currentZoom + ZoomStep);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) =>
        SetZoom(_currentZoom - ZoomStep);

    private void OnZoomResetClick(object sender, MouseButtonEventArgs e) =>
        SetZoom(1.0);

    #endregion

    #region Native Win32 & DWM Integration

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void ApplyDwmRoundedCorners()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
        }
        catch
        {
            // Gracefully ignore on OS versions without corner preference support
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
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

    #endregion

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
}

