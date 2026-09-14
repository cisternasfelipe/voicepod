using System.Windows;
using VoiceFlow.App.ViewModels;

namespace VoiceFlow.App.Views;

public partial class ModelSetupWindow : Window
{
    public ModelSetupWindow(ModelSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Finished += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            DialogResult = true;
            Close();
        });
    }
}
