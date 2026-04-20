using System.Net;
using Moq;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class CompositeRemotePrinterOperationsTests
{
    [Fact]
    public async Task ListPrinterQueuesAsync_WhenPrimaryEmpty_UsesFallback()
    {
        var primary = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var fallback = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        primary.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RemotePrinterQueueInfo>());
        fallback.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Q1", "PORT") });

        var sut = new CompositeRemotePrinterOperations(primary.Object, fallback.Object);
        var cred = new NetworkCredential("u", "p");
        var list = await sut.ListPrinterQueuesAsync("pc", cred);

        Assert.Single(list);
        Assert.Equal("Q1", list[0].Name);
        primary.VerifyAll();
        fallback.VerifyAll();
    }

    [Fact]
    public async Task ListPrinterQueuesAsync_WhenPrimaryHasRows_DoesNotCallFallback()
    {
        var primary = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var fallback = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        primary.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Q1", "PORT") });

        var sut = new CompositeRemotePrinterOperations(primary.Object, fallback.Object);
        var list = await sut.ListPrinterQueuesAsync("pc", new NetworkCredential("u", "p"));

        Assert.Single(list);
        fallback.Verify(p => p.ListPrinterQueuesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListPrinterQueuesAsync_WhenPrimaryThrows_UsesFallback()
    {
        var primary = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var fallback = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        primary.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("winrm down"));
        fallback.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Q1", "PORT") });

        var sut = new CompositeRemotePrinterOperations(primary.Object, fallback.Object);
        var list = await sut.ListPrinterQueuesAsync("pc", new NetworkCredential("u", "p"));

        Assert.Single(list);
        Assert.Equal("Q1", list[0].Name);
    }

    [Fact]
    public async Task ListPrinterQueuesAsync_WhenPrimaryThrowsAndFallbackEmpty_ThrowsPrimary()
    {
        var primary = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var fallback = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        primary.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("winrm down"));
        fallback.Setup(p => p.ListPrinterQueuesAsync("pc", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RemotePrinterQueueInfo>());

        var sut = new CompositeRemotePrinterOperations(primary.Object, fallback.Object);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ListPrinterQueuesAsync("pc", new NetworkCredential("u", "p")));

        Assert.Contains("WinRM", ex.Message);
        Assert.Contains("winrm down", ex.Message);
    }
}
