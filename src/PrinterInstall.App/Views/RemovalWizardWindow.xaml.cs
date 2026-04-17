using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class RemovalWizardWindow
{
    public RemovalWizardWindow(RemovalWizardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
