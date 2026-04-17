using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
