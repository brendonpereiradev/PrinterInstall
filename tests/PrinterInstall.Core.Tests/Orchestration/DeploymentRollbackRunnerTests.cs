using System.Net;
using Moq;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class DeploymentRollbackRunnerTests
{
    [Fact]
    public async Task RunAsync_QueueEntry_CallsRemoveQueue_ThenOrphanPort()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(r => r.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(r => r.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remote.Setup(r => r.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var control = new PrinterControlOrchestrator(remote.Object);
        var sut = new DeploymentRollbackRunner(remote.Object, control);
        var journal = new DeploymentRollbackJournal();
        journal.RecordQueueCreated("pc1", "Q1", "10.0.0.1");

        var cred = new NetworkCredential("u", "p");
        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(journal, cred, new InlineProgress<PrinterRemovalProgressEvent>(events.Add), CancellationToken.None);

        remote.Verify(r => r.RemovePrinterQueueAsync("pc1", cred, "Q1", It.IsAny<CancellationToken>()), Times.Once);
        remote.Verify(r => r.CountPrintersUsingPortAsync("pc1", cred, "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
        remote.Verify(r => r.RemoveTcpPrinterPortAsync("pc1", cred, "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_PortOnly_SkipsRemoveQueue_RemovesPortWhenUnused()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(r => r.CountPrintersUsingPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remote.Setup(r => r.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var control = new PrinterControlOrchestrator(remote.Object);
        var sut = new DeploymentRollbackRunner(remote.Object, control);
        var journal = new DeploymentRollbackJournal();
        journal.RecordPortCreated("pc1", "10.0.0.5");

        await sut.RunAsync(journal, new NetworkCredential("u", "p"), new Progress<PrinterRemovalProgressEvent>(_ => { }), CancellationToken.None);

        remote.Verify(r => r.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        remote.Verify(r => r.RemoveTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.5", It.IsAny<CancellationToken>()), Times.Once);
    }
}
