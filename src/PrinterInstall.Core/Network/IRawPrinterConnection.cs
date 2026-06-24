namespace PrinterInstall.Core.Network;

internal interface IRawPrinterConnection : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

internal interface IRawPrinterConnectionFactory
{
    IRawPrinterConnection Create();
}

internal sealed class TcpRawPrinterConnectionFactory : IRawPrinterConnectionFactory
{
    public IRawPrinterConnection Create() => new TcpRawPrinterConnection();
}
