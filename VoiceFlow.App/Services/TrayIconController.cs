using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Abstractions;

namespace VoiceFlow.App.Services;

/// <summary>
/// Owns the tray icon and its context menu. The menu is rebuilt whenever the profile list
/// changes so switching the active profile stays one click away.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly TaskbarIcon _icon;
    private bool _disposed;

    public TrayIconController(ISettingsService settings)
    {
        _settings = settings;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/VoiceFlow;component/Resources/voiceflow.ico", UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        _icon = new TaskbarIcon
        {
            ToolTipText = "VoiceFlow",
            IconSource = bitmap,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true
        };

        _icon.TrayLeftMouseUp += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        try
        {
            _icon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Warning: Tray icon ForceCreate failed", ex);
        }

        RebuildMenu();
        _settings.SettingsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RebuildMenu);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<string>? ProfileSelected;

    public void ShowMessage(string title, string message, bool isError = false) =>
        _icon.ShowNotification(
            title,
            message,
            isError ? NotificationIcon.Error : NotificationIcon.Info);

    public void UpdateToolTip(string text) => _icon.ToolTipText = text;

    private void RebuildMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateItem(Strings.TrayOpen, () => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());

        var profilesItem = new MenuItem { Header = Strings.TrayActiveProfile };
        var activeId = _settings.Current.ActiveProfileId;

        foreach (var profile in _settings.Current.Profiles)
        {
            var id = profile.Id;
            var item = new MenuItem
            {
                Header = profile.Name,
                IsCheckable = true,
                IsChecked = id == activeId,
                StaysOpenOnClick = false
            };

            item.Click += (_, _) => ProfileSelected?.Invoke(this, id);
            profilesItem.Items.Add(item);
        }

        menu.Items.Add(profilesItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem(Strings.TrayHistory, () => HistoryRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(CreateItem(Strings.TraySettings, () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem(Strings.TrayExit, () => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon.ContextMenu = menu;
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Dispose();
    }
}
