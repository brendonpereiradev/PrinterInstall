using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorTests
{
    private static PrinterQueueDefinition OnePrinter(PrinterBrand brand, string name = "P1", string host = "10.0.0.1") => new()
    {
        Brand = brand,
        DisplayName = name,
        PrinterHostAddress = host,
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw
    };

    [Fact]
    public async Task RunAsync_DriverMissing_AbortsWithAbortedDriverMissing()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Some Other Driver" });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Epson) },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        IProgress<DeploymentProgressEvent> progress = new InlineProgress<DeploymentProgressEvent>(events.Add);

        await sut.RunAsync(request, new DeploymentRollbackJournal(), progress);

        Assert.Contains(events, e => e.State == TargetMachineState.AbortedDriverMissing);
        mock.Verify(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_DriverPresent_CreatesPortAndPrinter()
    {
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = true
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        mock.Verify(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.5", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", expectedDriver, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.PrintTestPageAsync("pc1", It.IsAny<NetworkCredential>(), "Office", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
    }

    [Fact]
    public async Task RunAsync_PrintTestPageDisabled_SkipsTestPage_VerifiesNeverCalled()
    {
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        mock.Verify(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
    }

    [Fact]
    public async Task RunAsync_TestPageFails_StillReportsCompletedSuccess_WithWarning()
    {
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("spooler"));

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = true
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        var done = Assert.Single(events.Where(e => e is { State: TargetMachineState.CompletedSuccess, PrinterQueueName: "Office" }));
        Assert.Contains("test page failed", done.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spooler", done.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_AfterAddPrinter_RecordsQueueInJournal()
    {
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var journal = new DeploymentRollbackJournal();
        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Q1", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await sut.RunAsync(request, journal, new Progress<DeploymentProgressEvent>(_ => { }), CancellationToken.None);

        Assert.Single(journal.QueueEntries);
        Assert.Equal("Q1", journal.QueueEntries[0].PrinterName);
        Assert.Empty(journal.PortOnlyEntries);
    }

    [Fact]
    public async Task RunAsync_CancelledAfterPortCreation_LeavesPortOnlyInJournal()
    {
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var cts = new CancellationTokenSource();
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });

        var journal = new DeploymentRollbackJournal();
        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Q1", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.RunAsync(request, journal, new Progress<DeploymentProgressEvent>(_ => { }), cts.Token));

        Assert.Empty(journal.QueueEntries);
        Assert.Single(journal.PortOnlyEntries);
        Assert.Contains(journal.PortOnlyEntries, t => t.Computer == "pc1" && t.PortName == "10.0.0.5");
    }
}
