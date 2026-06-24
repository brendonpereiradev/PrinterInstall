using System.Net.Sockets;

namespace PrinterInstall.Core.Network;

internal sealed class TcpRawPrinterConnection : IRawPrinterConnection
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        await _client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        _stream = _client.GetStream();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected.");

        await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _client?.Dispose();
        _client = null;
    }
}
