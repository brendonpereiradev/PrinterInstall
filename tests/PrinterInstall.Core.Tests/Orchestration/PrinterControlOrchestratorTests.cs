using System.Net;
using Moq;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterControlOrchestratorTests
{
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    private static PrinterControlRequest ControlRequest(params PrinterControlTarget[] targets) => new()
    {
        DomainCredential = new NetworkCredential("u", "p"),
        Targets = targets
    };

    private static PrinterControlTarget Target(string computer, PrinterRenameItem[]? renames = null, PrinterRemovalQueueItem[]? removals = null) => new()
    {
        ComputerName = computer,
        Renames = renames ?? Array.Empty<PrinterRenameItem>(),
        QueuesToRemove = removals ?? Array.Empty<PrinterRemovalQueueItem>()
    };

    [Fact]
    public async Task RunAsync_RenameBeforeRemove_CallsRenameThenRemove()
    {
        var order = new List<string>();
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RenamePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "Old", "New", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("rename"))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.RemovePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "Rm", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("remove"))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new PrinterControlOrchestrator(mock.Object);
        await sut.RunAsync(
            ControlRequest(
                Target("pc1",
                    renames: [new PrinterRenameItem("Old", "New")],
                    removals: [new PrinterRemovalQueueItem("Rm", "P1")])),
            new SyncProgress<PrinterRemovalProgressEvent>(_ => { }));

        Assert.Equal(new[] { "rename", "remove" }, order);
    }

    [Fact]
    public async Task RunAsync_OnlyRenames_DoesNotTouchRemovalsOrPorts()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RenamePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterControlOrchestrator(mock.Object);
        await sut.RunAsync(
            ControlRequest(Target("pc1", renames: [new PrinterRenameItem("A", "B")])),
            new SyncProgress<PrinterRemovalProgressEvent>(_ => { }));

        mock.Verify(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
