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
    [Fact]
    public async Task RunAsync_DriverMissing_AbortsWithAbortedDriverMissing()
    {
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Some Other Driver" });

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Brand = PrinterBrand.Epson,
            DisplayName = "P1",
            PrinterHostAddress = "10.0.0.1",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        var progress = new Progress<DeploymentProgressEvent>(e => events.Add(e));

        await sut.RunAsync(request, progress);

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
            Brand = PrinterBrand.Lexmark,
            DisplayName = "Office",
            PrinterHostAddress = "10.0.0.5",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = true
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new Progress<DeploymentProgressEvent>(e => events.Add(e)));

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
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Brand = PrinterBrand.Lexmark,
            DisplayName = "Office",
            PrinterHostAddress = "10.0.0.5",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new InlineProgress<DeploymentProgressEvent>(events.Add));

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
            Brand = PrinterBrand.Lexmark,
            DisplayName = "Office",
            PrinterHostAddress = "10.0.0.5",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = true
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new InlineProgress<DeploymentProgressEvent>(events.Add));

        var done = Assert.Single(events.Where(e => e.State == TargetMachineState.CompletedSuccess));
        Assert.Contains("test page failed", done.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spooler", done.Message, StringComparison.OrdinalIgnoreCase);
    }
}
