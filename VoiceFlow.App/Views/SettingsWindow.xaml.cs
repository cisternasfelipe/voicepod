using System.Windows;
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

    private void OnRecordHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_captureSession is null)
        {
            StartRecordingHotkey();
        }
        else
        {
            StopRecordingHotkey();
        }
    }

    private void StartRecordingHotkey()
    {
        RecordingStatusText.Visibility = Visibility.Visible;
        RecordingStatusText.Text = Strings.SettingsHotkeyListening;
        RecordHotkeyButton.Content = Strings.Cancel;
        HotkeyBox.Text = "...";

        _captureSession = new HotkeyCaptureSession();
        _captureSession.HotkeyCaptured += (_, hotkey) => Dispatcher.BeginInvoke(() =>
        {
            _viewModel.SetHotkey(hotkey);
            StopRecordingHotkey();
        });
        _captureSession.PreviewUpdated += (_, preview) => Dispatcher.BeginInvoke(() =>
        {
            if (_captureSession is not null)
            {
                if (!string.IsNullOrWhiteSpace(preview.DisplayText))
                {
                    HotkeyBox.Text = preview.DisplayText;
                }
                RecordingStatusText.Text = preview.HintText;
            }
        });
        _captureSession.Cancelled += (_, _) => Dispatcher.BeginInvoke(StopRecordingHotkey);
        _captureSession.Start();
    }

    private void StopRecordingHotkey()
    {
        _captureSession?.Dispose();
        _captureSession = null;

        RecordingStatusText.Visibility = Visibility.Collapsed;
        RecordingStatusText.Text = Strings.SettingsHotkeyListening;
        RecordHotkeyButton.Content = Strings.SettingsHotkeyRecord;
        HotkeyBox.Text = _viewModel.HotkeyText;
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
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
