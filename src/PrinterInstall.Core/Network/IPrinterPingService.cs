namespace PrinterInstall.Core.Network;

public interface IPrinterPingService
{
    Task<bool> PingAsync(string hostOrIp, TimeSpan timeout, CancellationToken cancellationToken = default);
}
