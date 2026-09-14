using System.Windows;
using VoiceFlow.App.ViewModels;

namespace VoiceFlow.App.Views;

public partial class ModelPickerWindow : Window
{
    private readonly ModelPickerViewModel _viewModel;

    public ModelPickerWindow(ModelPickerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ModelChosen += (_, id) =>
        {
            SelectedModelId = id;
            DialogResult = true;
        };

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    /// <summary>Model the user picked, or null when the window was closed without choosing.</summary>
    public string? SelectedModelId { get; private set; }

    private void OnListDoubleClick(object sender, RoutedEventArgs e) => _viewModel.ChooseCommand.Execute(null);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
