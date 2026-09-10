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

    /// <summary>
    /// Exibe diálogo de confirmação antes de reiniciar o serviço Spooler e expurgar a fila em uma máquina.
    /// Retorna verdadeiro se o usuário confirmar o procedimento.
    /// </summary>
    Task<bool> ConfirmSpoolerResetAsync(string computerName);

    /// <summary>
    /// Exibe diálogo de aviso e confirmação caso haja suspeita de inversão entre o nome da impressora e o host/IP.
    /// Retorna verdadeiro se o operador optar por inverter automaticamente os campos e prosseguir.
    /// </summary>
    Task<bool> ConfirmInversionCorrectionAsync(IReadOnlyList<string> inversions);

    /// <summary>
    /// Exibe diálogo de aviso quando o operador tenta iniciar o deploy sem nenhum computador alvo configurado.
    /// </summary>
    Task ShowNoComputersWarningAsync();
}
