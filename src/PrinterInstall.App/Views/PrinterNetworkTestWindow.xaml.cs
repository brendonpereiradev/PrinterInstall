using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class PrinterNetworkTestWindow
{
    public PrinterNetworkTestWindow(PrinterNetworkTestViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
