using System.Windows;
using System.Windows.Input;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class RemovalWizardWindow
{
    public RemovalWizardWindow(RemovalWizardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Step0Computers_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (DataContext is not RemovalWizardViewModel vm)
            return;

        var cmd = vm.StartCommand;
        if (cmd.CanExecute(null))
            cmd.Execute(null);
    }
}
