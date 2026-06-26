using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorGainschaLabelTests
{
    private static PrinterQueueDefinition GainschaPrinter(
        GainschaLabelPreset? preset = GainschaLabelPreset.Paciente,
        string name = "Q1") => new()
    {
        Brand = PrinterBrand.Gainscha,
        DisplayName = name,
        PrinterHostAddress = "10.0.0.10",
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw,
        GainschaLabelPreset = preset
    };

    [Fact]
    public async Task Gainscha_LabelConfigSuccess_RecordsQueueAfterPreset()
    {
        var driver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { driver });
        remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.10", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", driver, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.ConfigureGainschaLabelPresetAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", GainschaLabelPreset.Paciente, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var journal = new DeploymentRollbackJournal();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { GainschaPrinter() },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await new PrinterDeploymentOrchestrator(remote.Object).RunAsync(
            request,
            journal,
            new InlineProgress<DeploymentProgressEvent>(_ => { }));

        Assert.Single(journal.QueueEntries);
        remote.Verify(m => m.ConfigureGainschaLabelPresetAsync(
            "pc1", It.IsAny<NetworkCredential>(), "Q1", GainschaLabelPreset.Paciente, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gainscha_LabelConfigFailure_RemovesQueueAndPort_ReportsError()
    {
        var driver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { driver });
        remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.10", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", driver, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.ConfigureGainschaLabelPresetAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", GainschaLabelPreset.Paciente, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("driverdata"));
        remote.Setup(m => m.RemovePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.CountPrintersUsingPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remote.Setup(m => m.RemoveTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var journal = new DeploymentRollbackJournal();
        var events = new List<DeploymentProgressEvent>();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { GainschaPrinter() },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await new PrinterDeploymentOrchestrator(remote.Object).RunAsync(
            request,
            journal,
            new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Empty(journal.QueueEntries);
        Assert.Empty(journal.PortOnlyEntries);
        Assert.Contains(events, e => e is { State: TargetMachineState.Error, PrinterQueueName: "Q1" }
            && e.Message.Contains("Revertido", StringComparison.OrdinalIgnoreCase));
        remote.Verify(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Gainscha_SkippedAlreadyExists_DoesNotCallConfigurePreset()
    {
        var driver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { driver });
        remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[] { GainschaPrinter() },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await new PrinterDeploymentOrchestrator(remote.Object).RunAsync(
            request,
            new DeploymentRollbackJournal(),
            new InlineProgress<DeploymentProgressEvent>(_ => { }));

        remote.Verify(m => m.ConfigureGainschaLabelPresetAsync(
            It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<GainschaLabelPreset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Lexmark_DoesNotCallConfigurePreset()
    {
        var driver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { driver });
        remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.10", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", driver, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var journal = new DeploymentRollbackJournal();
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Lexmark,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.10",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("u", "p"),
            PrintTestPage = false
        };

        await new PrinterDeploymentOrchestrator(remote.Object).RunAsync(
            request,
            journal,
            new InlineProgress<DeploymentProgressEvent>(_ => { }));

        Assert.Single(journal.QueueEntries);
        remote.Verify(m => m.ConfigureGainschaLabelPresetAsync(
            It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<GainschaLabelPreset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
