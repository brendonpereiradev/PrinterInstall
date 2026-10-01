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

    private sealed class GainschaInfoHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var (body, contentType) = request.RequestUri!.AbsolutePath switch
            {
                "/" => ("<a href='jb_status_look.shtml'>Printer Info</a>", "text/html"),
                "/jb_status_look.shtml" => ("<span id='print_model'>#</span><script>loadXMLDoc('/updata_message?', update);</script>", "text/html"),
                _ => ("GA-2408T;SERIAL-TEST;firmware;boot;203;0;0;0;Ready;", "text/plain")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
            });
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

    [Theory]
    [InlineData(PrinterBrand.Gainscha)]
    [InlineData(PrinterBrand.Epson)]
    public async Task RunAsync_GainschaDynamicWebIdentity_ContinuesOnlyWithMatchingDriver(PrinterBrand selected)
    {
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        if (selected == PrinterBrand.Gainscha)
        {
            remote.Setup(r => r.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(["Gainscha GA-2408T"]);
            remote.Setup(r => r.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            remote.Setup(r => r.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "192.0.2.10", 9100, "RAW", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            remote.Setup(r => r.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", "Gainscha GA-2408T", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            remote.Setup(r => r.ConfigureGainschaLabelPresetAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", GainschaLabelPreset.Lote, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        using var client = new HttpClient(new GainschaInfoHttpHandler());
        var identity = new NetworkPrinterIdentityService("", client);
        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = ["pc1"],
            Printers = [new PrinterQueueDefinition
            {
                Brand = selected,
                PrinterHostAddress = "192.0.2.10",
                DisplayName = "Etiqueta",
                PortNumber = 9100,
                Protocol = TcpPrinterProtocol.Raw,
                GainschaLabelPreset = GainschaLabelPreset.Lote
            }],
            DomainCredential = new NetworkCredential("u", "p")
        };
        var events = new List<DeploymentProgressEvent>();
        var logs = new List<string>();

        await Create(remote.Object, identity).RunAsync(request, new DeploymentRollbackJournal(),
            new InlineProgress<DeploymentProgressEvent>(events.Add), diagnosticLog: new InlineProgress<string>(logs.Add));

        if (selected == PrinterBrand.Gainscha)
        {
            Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
            remote.Verify(r => r.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
            remote.Verify(r => r.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", It.IsAny<CancellationToken>()), Times.Once);
            remote.Verify(r => r.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "192.0.2.10", 9100, "RAW", It.IsAny<CancellationToken>()), Times.Once);
            remote.Verify(r => r.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", "Gainscha GA-2408T", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            remote.Verify(r => r.ConfigureGainschaLabelPresetAsync("pc1", It.IsAny<NetworkCredential>(), "Etiqueta", GainschaLabelPreset.Lote, It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            Assert.Contains(events, e => e.State == TargetMachineState.PrinterIdentityMismatch);
        }
        Assert.Contains(logs, line => line.Contains("HTTP Gainscha Printer Info"));
        Assert.DoesNotContain(logs, line => line.Contains("SERIAL-TEST"));
        remote.VerifyNoOtherCalls();
    }
}
