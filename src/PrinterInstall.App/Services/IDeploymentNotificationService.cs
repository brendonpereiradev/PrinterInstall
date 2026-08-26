namespace PrinterInstall.App.Services;

/// <summary>
/// Contrato do serviço de notificação sonora e alertas de conclusão do processo de deploy.
/// </summary>
public interface IDeploymentNotificationService
{
    /// <summary>
    /// Emite notificação de conclusão com sucesso.
    /// </summary>
    void NotifySuccess();

    /// <summary>
    /// Emite notificação de conclusão com aviso, cancelamento ou resultado parcial.
    /// </summary>
    void NotifyWarning();

    /// <summary>
    /// Emite notificação de falha ou erro no deploy.
    /// </summary>
    void NotifyError();
}
