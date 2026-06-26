using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestServiceTests
{
    private sealed class FakeConnection : IRawPrinterConnection
    {
        public bool ShouldConnectFail { get; init; }
        public bool ShouldWriteFail { get; init; }
        public byte[]? Written { get; private set; }

        public Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldConnectFail)
                throw new TimeoutException("connect failed");
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldWriteFail)
                throw new IOException("write failed");
            Written = data.ToArray();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory : IRawPrinterConnectionFactory
    {
        public FakeConnection Next { get; set; } = new();
        public IRawPrinterConnection Create() => Next;
    }

    [Fact]
    public async Task RunAsync_WhenConnectFails_ReturnsConnectivityPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldConnectFail = true } };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.1", PrinterBrand.Epson);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Connectivity, result.FailedPhase);
        Assert.Contains("10.0.0.1:9100", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenConnectSucceedsButWriteFails_ReturnsSendPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldWriteFail = true } };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.2", PrinterBrand.Lexmark);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Send, result.FailedPhase);
        Assert.Contains("Conectou", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenBothSucceed_ReturnsSuccess()
    {
        var fake = new FakeConnection();
        var factory = new FakeFactory { Next = fake };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.3", PrinterBrand.Epson);

        Assert.True(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.None, result.FailedPhase);
        Assert.NotNull(fake.Written);
        Assert.NotEmpty(fake.Written!);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringConnect_ThrowsOperationCanceledException()
    {
        var factory = new FakeFactory();
        var sut = new DirectRawPrinterTestService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RunAsync("10.0.0.4", PrinterBrand.Gainscha, cancellationToken: cts.Token));
    }
}
