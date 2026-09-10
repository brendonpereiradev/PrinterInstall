using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorPingTests
{
    [Fact]
    public async Task RunAsync_WhenPrinterPingSucceeds_CreatesPortAndInstallsPrinter()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var remoteMock = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remoteMock.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        remoteMock.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(x => x.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.50", "10.0.0.50", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", epson, "10.0.0.50", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pingMock = new Mock<IPrinterPingService>();
        pingMock.Setup(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new PrinterDeploymentOrchestrator(
            remoteMock.Object,
            new NullLocalDriverPackageCatalog(),
            new DirectRawPrinterTestService(),
            new NullFastHostReachabilityChecker(),
            pingMock.Object,
            TransientRetryHelper.DefaultMaxAttempts,
            TransientRetryHelper.DefaultInitialDelay);

        var events = new List<DeploymentProgressEvent>();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.50",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("user", "pass")
        };

        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        pingMock.Verify(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        remoteMock.Verify(x => x.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.50", "10.0.0.50", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
        remoteMock.Verify(x => x.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", epson, "10.0.0.50", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess && e.PrinterQueueName == "Q1");
    }

    [Fact]
    public async Task RunAsync_WhenPrinterPingFails_ReportsErrorAndSkipsPortAndPrinterCreation()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var remoteMock = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remoteMock.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        remoteMock.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var pingMock = new Mock<IPrinterPingService>();
        pingMock.Setup(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new PrinterDeploymentOrchestrator(
            remoteMock.Object,
            new NullLocalDriverPackageCatalog(),
            new DirectRawPrinterTestService(),
            new NullFastHostReachabilityChecker(),
            pingMock.Object,
            TransientRetryHelper.DefaultMaxAttempts,
            TransientRetryHelper.DefaultInitialDelay);

        var events = new List<DeploymentProgressEvent>();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.50",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("user", "pass")
        };

        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        // Deve testar o ping
        pingMock.Verify(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);

        // NUNCA deve criar porta ou fila se o ping falhou
        remoteMock.Verify(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        remoteMock.Verify(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Deve emitir erro com mensagem amigável
        Assert.Contains(events, e => e.State == TargetMachineState.Error && e.PrinterQueueName == "Q1" && e.Message.Contains("não respondeu ao ping"));
    }

    [Fact]
    public async Task RunAsync_WhenFirstPrinterPingFails_SecondPrinterInstallsSuccessfully()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var remoteMock = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remoteMock.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        remoteMock.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(x => x.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.51", "10.0.0.51", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q2", epson, "10.0.0.51", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pingMock = new Mock<IPrinterPingService>();
        pingMock.Setup(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Q1 falha
        pingMock.Setup(p => p.PingAsync("10.0.0.51", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);  // Q2 responde

        var sut = new PrinterDeploymentOrchestrator(
            remoteMock.Object,
            new NullLocalDriverPackageCatalog(),
            new DirectRawPrinterTestService(),
            new NullFastHostReachabilityChecker(),
            pingMock.Object,
            TransientRetryHelper.DefaultMaxAttempts,
            TransientRetryHelper.DefaultInitialDelay);

        var events = new List<DeploymentProgressEvent>();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.50",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                },
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q2",
                    PrinterHostAddress = "10.0.0.51",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("user", "pass")
        };

        await sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        // Q1 falhou no ping
        Assert.Contains(events, e => e.State == TargetMachineState.Error && e.PrinterQueueName == "Q1" && e.Message.Contains("não respondeu ao ping"));

        // Q2 instalou com sucesso
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess && e.PrinterQueueName == "Q2");
    }

    [Fact]
    public async Task RunAsync_WhenPrinterPingCancelled_PropagatesOperationCanceledException()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var remoteMock = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remoteMock.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        remoteMock.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        using var cts = new CancellationTokenSource();
        var pingMock = new Mock<IPrinterPingService>();
        pingMock.Setup(p => p.PingAsync("10.0.0.50", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = new PrinterDeploymentOrchestrator(
            remoteMock.Object,
            new NullLocalDriverPackageCatalog(),
            new DirectRawPrinterTestService(),
            new NullFastHostReachabilityChecker(),
            pingMock.Object,
            TransientRetryHelper.DefaultMaxAttempts,
            TransientRetryHelper.DefaultInitialDelay);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.50",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("user", "pass")
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.RunAsync(request, new DeploymentRollbackJournal(), new InlineProgress<DeploymentProgressEvent>(_ => { }), cts.Token));
    }
}
