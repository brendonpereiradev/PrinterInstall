namespace PrinterInstall.Core.Network;

public sealed class NullPrinterPingService : IPrinterPingService
{
    private readonly bool _alwaysReachable;

    public NullPrinterPingService(bool alwaysReachable = true)
    {
        _alwaysReachable = alwaysReachable;
    }

    public Task<bool> PingAsync(string hostOrIp, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_alwaysReachable);
    }
}
