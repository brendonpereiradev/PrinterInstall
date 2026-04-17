using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

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
            SelectedModelId = "epson-default",
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

        var sut = new PrinterDeploymentOrchestrator(mock.Object);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Brand = PrinterBrand.Lexmark,
            SelectedModelId = "lexmark-default",
            DisplayName = "Office",
            PrinterHostAddress = "10.0.0.5",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new Progress<DeploymentProgressEvent>(e => events.Add(e)));

        mock.Verify(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.5", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", expectedDriver, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
    }
}
