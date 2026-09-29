using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestServiceTests
{
    private sealed class FakeIdentityService : IPrinterIdentityService
    {
        public Task<PrinterIdentityResult> IdentifyAsync(string host, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(host.EndsWith(".2", StringComparison.Ordinal)
                ? new PrinterIdentityResult(host, PrinterBrand.Lexmark, "CX532ADWE", "Teste")
                : new PrinterIdentityResult(host, PrinterBrand.Epson, "M1180", "Teste"));
        }
    }

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
        public bool Created { get; private set; }
        public IRawPrinterConnection Create()
        {
            Created = true;
            return Next;
        }
    }

    [Fact]
    public async Task RunAsync_WhenConnectFails_ReturnsConnectivityPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldConnectFail = true } };
        var sut = new DirectRawPrinterTestService(factory, new FakeIdentityService());

        var result = await sut.RunAsync("10.0.0.1", PrinterBrand.Epson);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Connectivity, result.FailedPhase);
        Assert.Equal("Sem conectividade com 10.0.0.1.", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenConnectSucceedsButWriteFails_ReturnsSendPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldWriteFail = true } };
        var sut = new DirectRawPrinterTestService(factory, new FakeIdentityService());

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
        var sut = new DirectRawPrinterTestService(factory, new FakeIdentityService());

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
        var sut = new DirectRawPrinterTestService(factory, new FakeIdentityService());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RunAsync("10.0.0.4", PrinterBrand.Gainscha, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAsync_MismatchedIdentity_DoesNotOpenRawConnection()
    {
        var connection = new FakeConnection();
        var factory = new FakeFactory { Next = connection };
        var sut = new DirectRawPrinterTestService(factory, new FakeIdentityService());

        var result = await sut.RunAsync("10.0.0.2", PrinterBrand.Epson);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Identity, result.FailedPhase);
        Assert.False(factory.Created);
        Assert.Null(connection.Written);
        Assert.Contains("Lexmark", result.Message);
    }
}
