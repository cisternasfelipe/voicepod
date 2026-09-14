using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using VoiceFlow.App.Resources;
using VoiceFlow.App.Services;
using VoiceFlow.App.ViewModels;
using VoiceFlow.Core.Models;
using VoiceFlow.Interop;

namespace VoiceFlow.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private HotkeyCaptureSession? _captureSession;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) => ApiKeyBox.Password = viewModel.ApiKey;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDwmRoundedCorners();
    }

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
            // Gracefully ignore on older Windows versions
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private enum HotkeyTargetSlot { None, Hold, Toggle }
    private HotkeyTargetSlot _recordingSlot = HotkeyTargetSlot.None;

    private void OnRecordHoldHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_recordingSlot == HotkeyTargetSlot.Hold)
        {
            StopRecordingHotkey();
        }
        else
        {
            StartRecordingSlot(HotkeyTargetSlot.Hold);
        }
    }

    private void OnRecordToggleHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_recordingSlot == HotkeyTargetSlot.Toggle)
        {
            StopRecordingHotkey();
        }
        else
        {
            StartRecordingSlot(HotkeyTargetSlot.Toggle);
        }
    }

    private void OnTabItemPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TabItem tabItem)
        {
            tabItem.IsSelected = true;
        }
    }

    private void StartRecordingSlot(HotkeyTargetSlot slot)
    {
        StopRecordingHotkey();
        _recordingSlot = slot;

        var box = slot == HotkeyTargetSlot.Hold ? HoldHotkeyBox : ToggleHotkeyBox;
        var btn = slot == HotkeyTargetSlot.Hold ? RecordHoldHotkeyButton : RecordToggleHotkeyButton;
        var status = slot == HotkeyTargetSlot.Hold ? HoldRecordingStatusText : ToggleRecordingStatusText;

        status.Visibility = Visibility.Visible;
        status.Text = Strings.SettingsHotkeyListening;
        btn.Content = Strings.Cancel;
        box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "...");

        _captureSession = new HotkeyCaptureSession();
        _captureSession.HotkeyCaptured += (_, hotkey) => Dispatcher.BeginInvoke(() =>
        {
            if (slot == HotkeyTargetSlot.Hold)
            {
                _viewModel.SetHoldHotkey(hotkey);
            }
            else
            {
                _viewModel.SetToggleHotkey(hotkey);
            }
            StopRecordingHotkey();
        });

        _captureSession.PreviewUpdated += (_, preview) => Dispatcher.BeginInvoke(() =>
        {
            if (_captureSession is not null)
            {
                if (!string.IsNullOrWhiteSpace(preview.DisplayText))
                {
                    box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, preview.DisplayText);
                }
                status.Text = preview.HintText;
            }
        });

        _captureSession.Cancelled += (_, _) => Dispatcher.BeginInvoke(StopRecordingHotkey);
        _captureSession.Start();
    }

    private void StopRecordingHotkey()
    {
        _captureSession?.Dispose();
        _captureSession = null;

        if (_recordingSlot != HotkeyTargetSlot.None)
        {
            HoldRecordingStatusText.Visibility = Visibility.Collapsed;
            ToggleRecordingStatusText.Visibility = Visibility.Collapsed;

            RecordHoldHotkeyButton.Content = Strings.SettingsHotkeyRecord;
            RecordToggleHotkeyButton.Content = Strings.SettingsHotkeyRecord;

            HoldHotkeyBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, _viewModel.HoldHotkeyText);
            ToggleHotkeyBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, _viewModel.ToggleHotkeyText);

            _recordingSlot = HotkeyTargetSlot.None;
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e) => _viewModel.ApiKey = ApiKeyBox.Password;

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveAsync();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        StopRecordingHotkey();
        base.OnClosed(e);
    }
}
