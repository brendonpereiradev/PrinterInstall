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

    public Task<bool> ConfirmSpoolerResetAsync(string computerName)
    {
        if (Application.Current is null)
            return Task.FromResult(true);

        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowSpoolerResetConfirmDialog(computerName));
        }

        return dispatcher.InvokeAsync(() => ShowSpoolerResetConfirmDialog(computerName)).Task;
    }

    public Task<bool> ConfirmInversionCorrectionAsync(IReadOnlyList<string> inversions)
    {
        if (inversions.Count == 0)
            return Task.FromResult(true);

        if (Application.Current is null)
            return Task.FromResult(true);

        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowInversionCorrectionDialog(inversions));
        }

        return dispatcher.InvokeAsync(() => ShowInversionCorrectionDialog(inversions)).Task;
    }

    private static bool ShowInversionCorrectionDialog(IReadOnlyList<string> inversions)
    {
        var dialog = new ConfirmationDialogWindow();
        var owner = GetActiveOrMainWindow();
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ConfigureForInversionWarning(
            UiStrings.Main_InversionDialogTitle,
            UiStrings.Main_InversionDialogHeader,
            inversions,
            UiStrings.Main_InversionDialogQuestion,
            UiStrings.Main_InversionDialogProceedButton,
            UiStrings.Main_InversionDialogCancelButton);

        var result = dialog.ShowDialog();
        return result == true;
    }

    public Task ShowNoComputersWarningAsync()
    {
        if (Application.Current is null)
            return Task.CompletedTask;

        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            ShowNoComputersWarningDialog();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(ShowNoComputersWarningDialog).Task;
    }

    private static void ShowNoComputersWarningDialog()
    {
        var dialog = new ConfirmationDialogWindow();
        var owner = GetActiveOrMainWindow();
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ConfigureForNoComputersAlert(
            UiStrings.Main_NoComputersDialogTitle,
            UiStrings.Main_NoComputersDialogHeader,
            new[] { UiStrings.Main_NoComputersDialogMessage },
            UiStrings.Main_NoComputersDialogButton);

        dialog.ShowDialog();
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

    private static bool ShowSpoolerResetConfirmDialog(string computerName)
    {
        var details = new List<string>
        {
            $"Computador alvo: {computerName.Trim()}",
            "Interrupção forçada do serviço Print Spooler",
            "Exclusão de arquivos temporários de spool (*.spl, *.shd)",
            "Inicialização e validação de status ativo do Spooler"
        };

        var dialog = new ConfirmationDialogWindow();
        var owner = GetActiveOrMainWindow();
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ConfigureForSpoolerReset(
            UiStrings.Removal_ResetSpoolerConfirmTitle,
            string.Format(UiStrings.Removal_ResetSpoolerConfirmMessage, computerName.Trim()),
            details,
            "Deseja prosseguir com o reinício do Spooler e limpeza da fila?",
            "Reiniciar Spooler",
            "Cancelar");

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
