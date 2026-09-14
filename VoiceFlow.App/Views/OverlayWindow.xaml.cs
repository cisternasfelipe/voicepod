using System.Windows;
using System.Windows.Interop;
using VoiceFlow.App.ViewModels;
using VoiceFlow.Interop;

namespace VoiceFlow.App.Views;

/// <summary>
/// Small status window pinned to the bottom-right corner. It is marked WS_EX_NOACTIVATE so
/// it never takes focus away from the window the text is going to be pasted into.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ScreenMargin = 24;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        WindowStyles.MakeNonActivating(handle);

        MoveToCorner();
    }

    /// <summary>Places the overlay in the bottom-right corner of the working area.</summary>
    public void MoveToCorner()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - ScreenMargin;
        Top = work.Bottom - ActualHeight - ScreenMargin;
    }

    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        MoveToCorner();
    }
}
