using System.Windows;
using System.Windows.Controls;
using VoiceFlow.App.ViewModels;

namespace VoiceFlow.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;

    public HistoryWindow(HistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    /// <summary>Opens the profile picker for the "reprocesar" action of one entry.</summary>
    private void OnReprocessClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryEntryViewModel entry } button)
        {
            return;
        }

        var menu = new ContextMenu();

        foreach (var profile in _viewModel.Profiles)
        {
            var item = new MenuItem { Header = profile.Name };
            var captured = profile;

            item.Click += async (_, _) =>
                await _viewModel.ReprocessAsync(new ReprocessRequest(entry, captured));

            menu.Items.Add(item);
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    /// <summary>Reloads the list after a new dictation was stored while the window is open.</summary>
    public void RefreshFromHost() => _ = _viewModel.RefreshAsync();

    public void ShowAndActivate()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }
}
