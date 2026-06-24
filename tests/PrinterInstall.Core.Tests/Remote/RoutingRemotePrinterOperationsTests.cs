using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RoutingRemotePrinterOperationsTests
{
    private static readonly NetworkCredential Cred = new("user", "pass", "domain");

    [Fact]
    public async Task GetInstalledDriverNamesAsync_LocalMachineName_DelegatesToLocal()
    {
        var identity = new LocalMachineIdentity();
        var local = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var expected = new[] { "Lexmark Universal v4 XL" };
        local.Setup(x => x.GetInstalledDriverNamesAsync(Environment.MachineName, Cred, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = new RoutingRemotePrinterOperations(identity, local.Object, remote.Object);
        var result = await sut.GetInstalledDriverNamesAsync(Environment.MachineName, Cred);

        Assert.Equal(expected, result);
        local.VerifyAll();
        remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetInstalledDriverNamesAsync_RemoteMachine_DelegatesToRemote()
    {
        var identity = new LocalMachineIdentity();
        var local = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        const string remotePc = "definitely-not-this-pc-xyz-99999";
        var expected = new[] { "Driver A" };
        remote.Setup(x => x.GetInstalledDriverNamesAsync(remotePc, Cred, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = new RoutingRemotePrinterOperations(identity, local.Object, remote.Object);
        var result = await sut.GetInstalledDriverNamesAsync(remotePc, Cred);

        Assert.Equal(expected, result);
        remote.VerifyAll();
        local.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemovePrinterQueueAsync_Localhost_DelegatesToLocal()
    {
        var identity = new LocalMachineIdentity();
        var local = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        local.Setup(x => x.RemovePrinterQueueAsync("localhost", Cred, "Printer1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new RoutingRemotePrinterOperations(identity, local.Object, remote.Object);
        await sut.RemovePrinterQueueAsync("localhost", Cred, "Printer1");

        local.VerifyAll();
        remote.VerifyNoOtherCalls();
    }
}
