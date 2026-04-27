using System.Net;
using Moq;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterRemovalOrchestratorTests
{
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    private static PrinterRemovalRequest Request(params PrinterRemovalTarget[] targets) => new()
    {
        DomainCredential = new NetworkCredential("u", "p"),
        Targets = targets
    };

    private static PrinterRemovalTarget Target(string computer, params PrinterRemovalQueueItem[] items) => new()
    {
        ComputerName = computer,
        QueuesToRemove = items
    };

    [Fact]
    public async Task RunAsync_EmptyQueueList_ReportsNothingToDoAndSkipsRemote()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));

        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(
            Request(Target("pc1")),
            new SyncProgress<PrinterRemovalProgressEvent>(e => events.Add(e)));

        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.TargetCompleted && e.Message.Contains("Nothing"));
        mock.Verify(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_PerQueueError_ContinuesWithRemainingQueues()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "A", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "B", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));

        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(
            Request(Target("pc1",
                new PrinterRemovalQueueItem("A", "P1"),
                new PrinterRemovalQueueItem("B", "P2"))),
            new SyncProgress<PrinterRemovalProgressEvent>(e => events.Add(e)));

        mock.Verify(m => m.RemovePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "A", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.RemovePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "B", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.Error && e.Message.Contains("A"));
        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.TargetCompleted);
    }

    [Fact]
    public async Task RunAsync_OrphanPort_RemovesPort()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "IP_10.0.0.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "IP_10.0.0.1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));
        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(
            Request(Target("pc1", new PrinterRemovalQueueItem("Q", "IP_10.0.0.1"))),
            new SyncProgress<PrinterRemovalProgressEvent>(e => events.Add(e)));

        mock.Verify(m => m.RemoveTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "IP_10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.RemovingOrphanPort);
    }

    [Fact]
    public async Task RunAsync_PortStillInUse_DoesNotRemovePort()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "IP_10.0.0.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));
        await sut.RunAsync(
            Request(Target("pc1", new PrinterRemovalQueueItem("Q", "IP_10.0.0.1"))),
            new SyncProgress<PrinterRemovalProgressEvent>(_ => { }));

        mock.Verify(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_OrphanPortRemovalFails_ReportsWarningAndContinues()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("nope"));

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));
        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(
            Request(Target("pc1", new PrinterRemovalQueueItem("Q", "P"))),
            new SyncProgress<PrinterRemovalProgressEvent>(e => events.Add(e)));

        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.Warning);
        Assert.Contains(events, e => e.State == PrinterRemovalProgressState.TargetCompleted);
    }

    [Fact]
    public async Task RunAsync_NullPortName_SkipsPortSteps()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));
        await sut.RunAsync(
            Request(Target("pc1", new PrinterRemovalQueueItem("Q", null))),
            new SyncProgress<PrinterRemovalProgressEvent>(_ => { }));

        mock.Verify(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Cancellation_PropagatesOperationCanceled()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = new PrinterRemovalOrchestrator(new PrinterControlOrchestrator(mock.Object));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.RunAsync(
                Request(Target("pc1", new PrinterRemovalQueueItem("Q", "P"))),
                new SyncProgress<PrinterRemovalProgressEvent>(_ => { })));
    }
}

