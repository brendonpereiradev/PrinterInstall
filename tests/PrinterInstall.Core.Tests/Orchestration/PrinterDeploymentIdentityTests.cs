using System.Net;
using Moq;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentIdentityTests
{
    private sealed class FakeIdentityService(IReadOnlyDictionary<string, PrinterIdentityResult> identities) : IPrinterIdentityService
    {
        public readonly Dictionary<string, int> Calls = new(StringComparer.OrdinalIgnoreCase);

        public Task<PrinterIdentityResult> IdentifyAsync(string host, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls[host] = Calls.GetValueOrDefault(host) + 1;
            return Task.FromResult(identities[host]);
        }
    }

    private static PrinterQueueDefinition Printer(string host, string name, PrinterBrand brand) => new()
    {
        PrinterHostAddress = host,
        DisplayName = name,
        Brand = brand,
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw
    };

    private static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote, IPrinterIdentityService identities) =>
        new(remote, new NullLocalDriverPackageCatalog(), new DirectRawPrinterTestService(identities),
            new NullFastHostReachabilityChecker(), new NullPrinterPingService(),
            TransientRetryHelper.DefaultMaxAttempts, TransientRetryHelper.DefaultInitialDelay,
            identityService: identities);

    [Fact]
    public async Task RunAsync_MismatchedLexmarkInMixedBatch_BlocksEveryMachineBeforeRemoteCalls()
    {
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var identity = new FakeIdentityService(new Dictionary<string, PrinterIdentityResult>
        {
            ["192.0.2.10"] = new("192.0.2.10", PrinterBrand.Epson, "M1180", "SNMP"),
            ["192.0.2.20"] = new("192.0.2.20", PrinterBrand.Lexmark, "CX532ADWE", "SNMP")
        });
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = ["pc1", "pc2"],
            Printers = [Printer("192.0.2.10", "Correta", PrinterBrand.Epson),
                Printer("192.0.2.20", "Errada", PrinterBrand.Epson)],
            DomainCredential = new NetworkCredential("u", "p")
        };
        var events = new List<DeploymentProgressEvent>();
        var journal = new DeploymentRollbackJournal();

        await Create(remote.Object, identity).RunAsync(request, journal,
            new InlineProgress<DeploymentProgressEvent>(events.Add));

        remote.VerifyNoOtherCalls();
        Assert.False(journal.HasRollbackWork);
        Assert.Equal(2, identity.Calls.Count);
        Assert.Contains(events, e => e.ComputerName == "pc1" && e.PrinterQueueName == "Errada"
            && e.State == TargetMachineState.PrinterIdentityMismatch && e.Message.Contains("Lexmark")
            && e.Detail!.Contains("A impressora 192.0.2.20 foi identificada"));
        Assert.Contains(events, e => e.ComputerName == "pc2" && e.PrinterQueueName == "Correta"
            && e.State == TargetMachineState.DeployCancelled);
    }

    [Fact]
    public async Task RunAsync_RepeatedHostIsQueriedOnceAndComparedAgainstBothSelections()
    {
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var host = "192.0.2.30";
        var identity = new FakeIdentityService(new Dictionary<string, PrinterIdentityResult>
        {
            [host] = new(host, PrinterBrand.Lexmark, "CX532ADWE", "IPP")
        });
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = ["pc1"],
            Printers = [Printer(host, "A", PrinterBrand.Lexmark), Printer(host, "B", PrinterBrand.Brother)],
            DomainCredential = new NetworkCredential("u", "p")
        };
        var events = new List<DeploymentProgressEvent>();

        await Create(remote.Object, identity).RunAsync(request, new DeploymentRollbackJournal(),
            new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Equal(1, identity.Calls[host]);
        remote.VerifyNoOtherCalls();
        Assert.Contains(events, e => e.PrinterQueueName == "B" && e.State == TargetMachineState.PrinterIdentityMismatch);
    }

    [Fact]
    public async Task RunAsync_UnknownIdentity_BlocksWithoutRemoteCalls()
    {
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var host = "192.0.2.40";
        var identity = new FakeIdentityService(new Dictionary<string, PrinterIdentityResult>
        {
            [host] = new(host, null, null, "SNMP/IPP", "Nenhuma resposta")
        });
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = ["pc1"],
            Printers = [Printer(host, "Fila", PrinterBrand.Epson)],
            DomainCredential = new NetworkCredential("u", "p")
        };
        var events = new List<DeploymentProgressEvent>();

        await Create(remote.Object, identity).RunAsync(request, new DeploymentRollbackJournal(),
            new InlineProgress<DeploymentProgressEvent>(events.Add));

        remote.VerifyNoOtherCalls();
        Assert.Contains(events, e => e.State == TargetMachineState.PrinterIdentityUnknown
            && e.Message == "Modelo da impressora não identificado."
            && e.Detail!.Contains("Nenhuma resposta"));
    }

    [Fact]
    public async Task RunAsync_ApprovedIdentity_ContinuesIntoExistingDriverChecks()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(r => r.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["EPSON Universal Print Driver"]);
        remote.Setup(r => r.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Fila", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var host = "192.0.2.50";
        var identity = new FakeIdentityService(new Dictionary<string, PrinterIdentityResult>
        {
            [host] = new(host, PrinterBrand.Epson, "M1180", "SNMP")
        });
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = ["pc1"],
            Printers = [Printer(host, "Fila", PrinterBrand.Epson)],
            DomainCredential = new NetworkCredential("u", "p")
        };
        var events = new List<DeploymentProgressEvent>();

        await Create(remote.Object, identity).RunAsync(request, new DeploymentRollbackJournal(),
            new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.SkippedAlreadyExists);
        remote.Verify(r => r.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
