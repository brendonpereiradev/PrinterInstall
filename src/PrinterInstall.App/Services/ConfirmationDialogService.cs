using System.Windows;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Views;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Services;

public class ConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmDeployWarningAsync(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return Task.FromResult(true);

        if (Application.Current is null)
            return Task.FromResult(true);

        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowDeployWarningDialog(warnings));
        }

        return dispatcher.InvokeAsync(() => ShowDeployWarningDialog(warnings)).Task;
    }

    public Task<bool> ConfirmNetworkTestAsync(string hostAddress, PrinterBrand brand, GainschaLabelPreset? preset)
    {
        if (Application.Current is null)
            return Task.FromResult(true);

        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowNetworkTestConfirmDialog(hostAddress, brand, preset));
        }

        return dispatcher.InvokeAsync(() => ShowNetworkTestConfirmDialog(hostAddress, brand, preset)).Task;
    }

    private static bool ShowDeployWarningDialog(IReadOnlyList<string> warnings)
    {
        var dialog = new ConfirmationDialogWindow();
        var owner = GetActiveOrMainWindow();
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ConfigureForDeployWarning(
            UiStrings.Main_DeployWarningDialogTitle,
            UiStrings.Main_DeployWarningHeader,
            warnings,
            UiStrings.Main_DeployWarningQuestion,
            UiStrings.Main_DeployWarningProceedButton,
            UiStrings.Main_DeployWarningCancelButton);

        var result = dialog.ShowDialog();
        return result == true;
    }

    private static bool ShowNetworkTestConfirmDialog(string hostAddress, PrinterBrand brand, GainschaLabelPreset? preset)
    {
        var details = new List<string>
        {
            string.Format(UiStrings.NetworkTest_ConfirmHostFormat, hostAddress.Trim()),
            string.Format(UiStrings.NetworkTest_ConfirmBrandFormat, brand)
        };

        if (brand == PrinterBrand.Gainscha && preset.HasValue)
        {
            var def = GainschaLabelPresetCatalog.GetDefinition(preset.Value);
            details.Add(string.Format(UiStrings.NetworkTest_ConfirmPresetFormat, def.UiDisplayName));
        }

        var dialog = new ConfirmationDialogWindow();
        var owner = GetActiveOrMainWindow();
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ConfigureForNetworkTest(
            UiStrings.NetworkTest_ConfirmDialogTitle,
            UiStrings.NetworkTest_ConfirmHeader,
            details,
            UiStrings.NetworkTest_ConfirmProceedButton,
            UiStrings.NetworkTest_ConfirmCancelButton);

        var result = dialog.ShowDialog();
        return result == true;
    }

    private static Window? GetActiveOrMainWindow()
    {
        if (Application.Current is null)
            return null;

        return Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;
    }
}
