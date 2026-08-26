namespace PrinterInstall.App.Services;

/// <summary>
/// Contrato para detecção automática do domínio da máquina local.
/// </summary>
public interface IDomainDetector
{
    /// <summary>
    /// Tenta detectar o domínio de rede ou Active Directory ao qual o computador pertence.
    /// Retorna null se a máquina for standalone ou não pertencer a um domínio detectável.
    /// </summary>
    string? DetectCurrentDomain();
}
