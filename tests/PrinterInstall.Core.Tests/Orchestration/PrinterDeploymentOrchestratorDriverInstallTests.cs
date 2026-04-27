using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorDriverInstallTests
{
    private static PrinterQueueDefinition P(PrinterBrand brand) => new()
    {
        Brand = brand,
        DisplayName = "P1",
        PrinterHostAddress = "10.0.0.10",
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw
    };

    private static PrinterDeploymentRequest MakeRequest(PrinterBrand brand = PrinterBrand.Gainscha, IReadOnlyList<string>? targets = null, bool printTestPage = false) => new()
    {
        TargetComputerNames = targets ?? new[] { "pc1" },
        Printers = new[] { P(brand) },
        DomainCredential = new NetworkCredential("u", "p"),
        PrintTestPage = printTestPage
    };

    private static LocalDriverPackage MakePackage(PrinterBrand brand) =>
        new(brand, "C:\\fake\\Drivers\\" + brand, "fake.inf", PrinterCatalog.GetExpectedDriverName(brand));

    private static Mock<ILocalDriverPackageCatalog> CatalogWith(PrinterBrand brand, LocalDriverPackage? package)
    {
        var mock = new Mock<ILocalDriverPackageCatalog>();
        mock.Setup(c => c.TryGet(brand)).Returns(package);
        return mock;
    }

    [Fact]
    public async Task DriverMissing_PackageAvailable_InstallSucceeds_Reconfirms_ContinuesFlow()
    {
        var expected = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var calls = 0;
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => ++calls == 1 ? (IReadOnlyList<string>)new[] { "Other" } : new[] { expected });
        remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "P1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.InstallPrinterDriverAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        remote.Setup(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.10", 9100, "RAW", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "P1", expected, It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        remote.Setup(m => m.PrintTestPageAsync("pc1", It.IsAny<NetworkCredential>(), "P1", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));

        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(printTestPage: true), new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.InstallingDriver);
        Assert.Contains(events, e => e.State == TargetMachineState.DriverInstalledReconfirming);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
        remote.VerifyAll();
    }

    [Fact]
    public async Task DriverMissing_PackageAvailable_InstallSucceeds_RevalidationFails_Aborts()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Still Wrong" });
        remote.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.AbortedDriverMissing);
        remote.Verify(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DriverMissing_NoLocalPackage_AbortsAsBefore()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });
        remote.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var catalog = CatalogWith(PrinterBrand.Gainscha, null);
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.AbortedDriverMissing);
        remote.Verify(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstallThrowsNotImplemented_AbortsWithChannelMessage()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });
        remote.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new NotImplementedException());

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new InlineProgress<DeploymentProgressEvent>(events.Add));

        var aborted = Assert.Single(events.Where(e => e is { State: TargetMachineState.AbortedDriverMissing, PrinterQueueName: "P1" }));
        Assert.Contains("install unsupported on this channel", aborted.Message);
    }

    [Fact]
    public async Task InstallThrowsGenericException_MapsToError_ContinuesRunForOtherTargets()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });
        remote.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var seq = new Queue<Func<Task>>(new Func<Task>[]
        {
            () => throw new InvalidOperationException("boom"),
            () => Task.CompletedTask
        });
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(() => seq.Dequeue()());

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var request = MakeRequest(targets: new[] { "pc1", "pc2" });
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(request, new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e is { ComputerName: "pc1", State: TargetMachineState.AbortedDriverMissing, PrinterQueueName: "P1" });
        Assert.Contains(events, e => e is { ComputerName: "pc2", State: TargetMachineState.InstallingDriver } or { ComputerName: "pc2", State: TargetMachineState.CompletedSuccess });
    }
}
