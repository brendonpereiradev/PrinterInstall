using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Services;

/// <summary>
/// Interface para exibição de diálogos modais de confirmação na interface do usuário.
/// </summary>
public interface IConfirmationDialogService
{
    /// <summary>
    /// Exibe diálogo de confirmação/aviso caso haja suspeita de divergência entre a marca/driver e o nome da impressora.
    /// Retorna verdadeiro se o usuário optar por prosseguir com o deploy mesmo assim.
    /// </summary>
    Task<bool> ConfirmDeployWarningAsync(IReadOnlyList<string> warnings);

    /// <summary>
    /// Exibe diálogo de confirmação antes de disparar o teste raw na porta 9100.
    /// Retorna verdadeiro se o usuário confirmar o envio.
    /// </summary>
    Task<bool> ConfirmNetworkTestAsync(string hostAddress, PrinterBrand brand, GainschaLabelPreset? preset);
}
