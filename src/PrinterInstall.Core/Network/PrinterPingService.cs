using System.Net.NetworkInformation;

namespace PrinterInstall.Core.Network;

public sealed class PrinterPingService : IPrinterPingService
{
    public async Task<bool> PingAsync(string hostOrIp, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var ping = new Ping();
            var timeoutMs = Math.Max(100, (int)timeout.TotalMilliseconds);

            // Em .NET 8, SendPingAsync aceita timeout e CancellationToken via PingSender / Task.WaitAsync
            var pingTask = ping.SendPingAsync(hostOrIp.Trim(), timeoutMs);
            var reply = await pingTask.WaitAsync(cancellationToken).ConfigureAwait(false);

            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Qualquer falha de rede, resolução de nomes (DNS) ou host inalcançável resulta em false
            return false;
        }
    }
}
