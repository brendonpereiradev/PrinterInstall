using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorRetryTests
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
    public async Task RunAsync_InitialDriverProbeTransientTimeout_RetriesAndSucceeds()
    {
        // Arrange
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        var calls = 0;

        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                if (calls == 1)
                    throw new TimeoutException("Falha transitória na conexão WMI.");
                return Task.FromResult<IReadOnlyList<string>>(new[] { expectedDriver });
            });

        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Usamos atraso zero para execução rápida no teste unitário
        var sut = new PrinterDeploymentOrchestrator(mock.Object, new NullLocalDriverPackageCatalog(), maxRetryAttempts: 2, retryDelay: TimeSpan.Zero);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        var progress = new InlineProgress<DeploymentProgressEvent>(events.Add);

        // Act
        await sut.RunAsync(request, new DeploymentRollbackJournal(), progress);

        // Assert
        Assert.Equal(2, calls);
        mock.Verify(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.5", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", expectedDriver, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == TargetMachineState.ContactingRemote && e.Message.Contains("Falha transitória"));
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
    }

    [Fact]
    public async Task RunAsync_InitialDriverProbeTransientTimeout_ExhaustsRetries_ReportsError()
    {
        // Arrange
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Timeout persistente."));

        var sut = new PrinterDeploymentOrchestrator(mock.Object, new NullLocalDriverPackageCatalog(), maxRetryAttempts: 2, retryDelay: TimeSpan.Zero);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark) },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        var progress = new InlineProgress<DeploymentProgressEvent>(events.Add);

        // Act
        await sut.RunAsync(request, new DeploymentRollbackJournal(), progress);

        // Assert
        mock.Verify(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        mock.Verify(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(events, e => e.State == TargetMachineState.Error && e.Message.Contains("Timeout persistente"));
    }

    [Fact]
    public async Task RunAsync_InitialDriverProbeNonTransientError_FailsImmediatelyWithoutRetry()
    {
        // Arrange
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Erro de estado não transitório."));

        var sut = new PrinterDeploymentOrchestrator(mock.Object, new NullLocalDriverPackageCatalog(), maxRetryAttempts: 3, retryDelay: TimeSpan.Zero);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark) },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        var progress = new InlineProgress<DeploymentProgressEvent>(events.Add);

        // Act
        await sut.RunAsync(request, new DeploymentRollbackJournal(), progress);

        // Assert
        mock.Verify(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == TargetMachineState.Error && e.Message.Contains("Erro de estado não transitório"));
    }

    [Fact]
    public async Task RunAsync_PrinterQueueExistsProbeTransientTimeout_RetriesAndSucceeds()
    {
        // Arrange
        var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var mock = new Mock<IRemotePrinterOperations>();
        mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expectedDriver });

        var queueCalls = 0;
        mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                queueCalls++;
                if (queueCalls == 1)
                    throw new TimeoutException("Timeout temporário ao checar Win32_Printer");
                return Task.FromResult(false);
            });

        mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterDeploymentOrchestrator(mock.Object, new NullLocalDriverPackageCatalog(), maxRetryAttempts: 2, retryDelay: TimeSpan.Zero);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var events = new List<DeploymentProgressEvent>();
        var progress = new InlineProgress<DeploymentProgressEvent>(events.Add);

        // Act
        await sut.RunAsync(request, new DeploymentRollbackJournal(), progress);

        // Assert
        Assert.Equal(2, queueCalls);
        mock.Verify(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.5", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", expectedDriver, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
    }
}
