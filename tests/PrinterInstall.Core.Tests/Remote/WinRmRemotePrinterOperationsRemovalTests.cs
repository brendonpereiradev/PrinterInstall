using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class WinRmRemotePrinterOperationsRemovalTests
{
    [Fact]
    public async Task ListPrinterQueuesAsync_UsesGetPrinterAndConvertToJson()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { """[{"Name":"Q","PortName":"P"}]""" });

        var stager = new Mock<IRemoteDriverFileStager>().Object;
        var sut = new WinRmRemotePrinterOperations(mock.Object, stager);
        var cred = new NetworkCredential("DOM\\u", "p");
        var list = await sut.ListPrinterQueuesAsync("pc1", cred);

        Assert.Single(list);
        Assert.Equal("Q", list[0].Name);
        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", cred, It.Is<string>(s => s.Contains("Get-Printer") && s.Contains("ConvertTo-Json")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovePrinterQueueAsync_UsesRemovePrinterWhenPresent()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var stager = new Mock<IRemoteDriverFileStager>().Object;
        var sut = new WinRmRemotePrinterOperations(mock.Object, stager);
        await sut.RemovePrinterQueueAsync("pc1", new NetworkCredential("u", "p"), "MyQueue");

        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.Is<string>(s => s.Contains("Remove-Printer") && s.Contains("MyQueue")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CountPrintersUsingPortAsync_ParsesLastLineAsInt()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "3" });

        var stager = new Mock<IRemoteDriverFileStager>().Object;
        var sut = new WinRmRemotePrinterOperations(mock.Object, stager);
        var count = await sut.CountPrintersUsingPortAsync("pc1", new NetworkCredential("u", "p"), "IP_10.0.0.1");

        Assert.Equal(3, count);
        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", It.IsAny<NetworkCredential>(), It.Is<string>(s => s.Contains("Where-Object") && s.Contains("IP_10.0.0.1")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveTcpPrinterPortAsync_UsesRemovePrinterPort()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var stager = new Mock<IRemoteDriverFileStager>().Object;
        var sut = new WinRmRemotePrinterOperations(mock.Object, stager);
        await sut.RemoveTcpPrinterPortAsync("pc1", new NetworkCredential("u", "p"), "IP_10.0.0.1");

        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", It.IsAny<NetworkCredential>(), It.Is<string>(s => s.Contains("Remove-PrinterPort") && s.Contains("IP_10.0.0.1")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
