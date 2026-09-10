namespace PrinterInstall.Core.Remote;

/// <summary>
/// Implementação nula de IFastHostReachabilityChecker que sempre considera os hosts acessíveis.
/// Útil para testes automatizados com mocks de rede.
/// </summary>
public sealed class NullFastHostReachabilityChecker : IFastHostReachabilityChecker
{
    public Task<(bool IsReachable, string? ErrorDetail)> CheckReachabilityAsync(string host, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((true, (string?)null));
    }
}
