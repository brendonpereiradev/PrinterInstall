using System;
using System.Media;

namespace PrinterInstall.App.Services;

/// <summary>
/// Implementação do serviço de notificação sonora ao término do deploy utilizando sons do sistema operacional.
/// </summary>
public sealed class DeploymentNotificationService : IDeploymentNotificationService
{
    /// <summary>
    /// Emite alerta sonoro de sucesso utilizando o som Asterisk do sistema operacional.
    /// </summary>
    public void NotifySuccess()
    {
        PlaySoundSafe(() => SystemSounds.Asterisk.Play());
    }

    /// <summary>
    /// Emite alerta sonoro de aviso ou cancelamento utilizando o som Exclamation do sistema operacional.
    /// </summary>
    public void NotifyWarning()
    {
        PlaySoundSafe(() => SystemSounds.Exclamation.Play());
    }

    /// <summary>
    /// Emite alerta sonoro de erro utilizando o som Hand (parada crítica) do sistema operacional.
    /// </summary>
    public void NotifyError()
    {
        PlaySoundSafe(() => SystemSounds.Hand.Play());
    }

    private static void PlaySoundSafe(Action playAction)
    {
        try
        {
            playAction();
        }
        catch
        {
            // Trata exceções de reprodução de áudio silenciosamente para não interromper a aplicação
        }
    }
}
