using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class WinRmRemotePrinterOperationsTests
{
    [Fact]
    public async Task GetInstalledDriverNamesAsync_InvokesScriptContainingGetPrinterDriver()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "EPSON Universal Print Driver" });

        var sut = new WinRmRemotePrinterOperations(mock.Object);
        var cred = new NetworkCredential("DOM\\admin", "x");
        var names = await sut.GetInstalledDriverNamesAsync("pc1", cred);

        Assert.Single(names);
        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", cred, It.Is<string>(s => s.Contains("Get-PrinterDriver")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
