using System.Net.Sockets;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Sondagem assíncrona ultra-rápida de conectividade com computadores remotos nas portas TCP de gerenciamento Windows (RPC 135 e SMB 445).
/// Evita timeouts longos de negociação WMI/DCOM em computadores offline ou bloqueados por firewall.
/// </summary>
public sealed class FastHostReachabilityChecker : IFastHostReachabilityChecker
{
    private static readonly int[] WindowsManagementPorts = { 135, 445 };
    private readonly TimeSpan _probeTimeout;

    public FastHostReachabilityChecker(TimeSpan? probeTimeout = null)
    {
        _probeTimeout = probeTimeout ?? TimeSpan.FromMilliseconds(1500);
    }

    public async Task<(bool IsReachable, string? ErrorDetail)> CheckReachabilityAsync(string host, CancellationToken cancellationToken = default)
    {
        var trimmed = host.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (false, "Nome de computador vazio.");

        // Máquina local é sempre considerada acessível
        if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }

        // Tenta conectar em paralelo nas portas 135 e 445
        var probeTasks = WindowsManagementPorts
            .Select(port => ProbeSinglePortAsync(trimmed, port, _probeTimeout, cancellationToken))
            .ToList();

        while (probeTasks.Count > 0)
        {
            var finished = await Task.WhenAny(probeTasks).ConfigureAwait(false);
            probeTasks.Remove(finished);

            var ok = await finished.ConfigureAwait(false);
            if (ok)
            {
                return (true, null);
            }
        }

        return (false, "Host inacessível");
    }

    private static async Task<bool> ProbeSinglePortAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
