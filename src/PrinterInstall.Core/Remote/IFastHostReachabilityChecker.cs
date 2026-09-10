namespace PrinterInstall.Core.Remote;

/// <summary>
/// Contrato para validação rápida de conectividade com computadores remotos antes do handshake WMI/CIM.
/// </summary>
public interface IFastHostReachabilityChecker
{
    /// <summary>
    /// Verifica de forma assíncrona e rápida se o host remoto está acessível na rede nas portas de gerenciamento (ex: RPC/SMB).
    /// </summary>
    /// <param name="host">Nome do computador ou endereço IP.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tupla indicando se o host está acessível e detalhes do erro caso inacessível.</returns>
    Task<(bool IsReachable, string? ErrorDetail)> CheckReachabilityAsync(string host, CancellationToken cancellationToken = default);
}
