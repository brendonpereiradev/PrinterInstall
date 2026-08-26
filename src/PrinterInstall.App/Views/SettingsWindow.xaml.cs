using System.Windows;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class SettingsWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TrySave())
        {
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
