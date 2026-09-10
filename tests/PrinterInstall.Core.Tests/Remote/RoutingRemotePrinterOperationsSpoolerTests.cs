using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RoutingRemotePrinterOperationsSpoolerTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData(".")]
    public async Task ResetSpoolerServiceAsync_LocalTarget_RoutesToLocalOperations(string target)
    {
        var cred = new NetworkCredential("user", "pass", "dom");
        var localMock = new Mock<IRemotePrinterOperations>();
        localMock.Setup(m => m.ResetSpoolerServiceAsync(target, cred, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpoolerResetResult.Success("OK local"));

        var remoteMock = new Mock<IRemotePrinterOperations>();

        var identity = new LocalMachineIdentity();
        var sut = new RoutingRemotePrinterOperations(identity, localMock.Object, remoteMock.Object);

        var result = await sut.ResetSpoolerServiceAsync(target, cred, purgeJobs: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("OK local", result.Message);
        localMock.Verify(m => m.ResetSpoolerServiceAsync(target, cred, true, It.IsAny<CancellationToken>()), Times.Once);
        remoteMock.Verify(m => m.ResetSpoolerServiceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetSpoolerServiceAsync_RemoteTarget_RoutesToRemoteOperations()
    {
        const string target = "NOTE-998877";
        var cred = new NetworkCredential("user", "pass", "dom");
        var localMock = new Mock<IRemotePrinterOperations>();
        var remoteMock = new Mock<IRemotePrinterOperations>();
        remoteMock.Setup(m => m.ResetSpoolerServiceAsync(target, cred, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpoolerResetResult.Success("OK remoto"));

        var identity = new LocalMachineIdentity();
        var sut = new RoutingRemotePrinterOperations(identity, localMock.Object, remoteMock.Object);

        var result = await sut.ResetSpoolerServiceAsync(target, cred, purgeJobs: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("OK remoto", result.Message);
        remoteMock.Verify(m => m.ResetSpoolerServiceAsync(target, cred, true, It.IsAny<CancellationToken>()), Times.Once);
        localMock.Verify(m => m.ResetSpoolerServiceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
