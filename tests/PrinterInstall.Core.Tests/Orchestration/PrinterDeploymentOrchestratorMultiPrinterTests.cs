using System.IO;
using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorMultiPrinterTests
{
    [Fact]
    public async Task RunAsync_QueueExists_SkipsPortAndAdd()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var m = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        m.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        m.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        m.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        m.Setup(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.2", 9100, "RAW", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m.Setup(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "Q2", epson, "10.0.0.2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PrinterDeploymentOrchestrator(m.Object);
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
                    PrinterHostAddress = "10.0.0.1",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                },
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q2",
                    PrinterHostAddress = "10.0.0.2",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("u", "p")
        };
        await sut.RunAsync(request, new InlineProgress<DeploymentProgressEvent>(events.Add));

        m.Verify(
            x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.1", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        m.Verify(
            x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "Q1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains(events, e => e is { State: TargetMachineState.SkippedAlreadyExists, PrinterQueueName: "Q1" });
    }

    [Fact]
    public async Task RunAsync_ProcessesPcsInOrder_AndListDriversOncePerMachine()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var m = new Mock<IRemotePrinterOperations>();
        m.Setup(x => x.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        m.Setup(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m.Setup(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var getCalls = new List<string>();
        m.Setup(x => x.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .Callback((string c, NetworkCredential _, CancellationToken __) => getCalls.Add(c))
            .ReturnsAsync(new[] { epson });

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "A", "B" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q1",
                    PrinterHostAddress = "10.0.0.1",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                },
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Q2",
                    PrinterHostAddress = "10.0.0.2",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var sut = new PrinterDeploymentOrchestrator(m.Object);
        var doneOrder = new List<(string Pc, string Q)>();
        await sut.RunAsync(
            request,
            new Progress<DeploymentProgressEvent>(e =>
            {
                if (e is { State: TargetMachineState.CompletedSuccess, PrinterQueueName: { } n })
                {
                    if (!string.IsNullOrEmpty(n))
                        doneOrder.Add((e.ComputerName, n));
                }
            }));

        Assert.Equal(
            new[] { ("A", "Q1"), ("A", "Q2"), ("B", "Q1"), ("B", "Q2") },
            doneOrder);
        Assert.Equal(2, getCalls.Distinct().Count());
        Assert.Equal(2, getCalls.Count);
    }

    [Fact]
    public async Task RunAsync_AddFailure_ContinuesWithNextPrinter()
    {
        var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        var m = new Mock<IRemotePrinterOperations>();
        m.Setup(x => x.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { epson });
        m.Setup(x => x.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        m.Setup(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var seq = new Queue<Func<Task>>(new[] { (Func<Task>)(() => throw new IOException("e1")), () => Task.CompletedTask });
        m.Setup(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => seq.Dequeue()());

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = new[] { "pc1" },
            Printers = new[]
            {
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Bad",
                    PrinterHostAddress = "10.0.0.1",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                },
                new PrinterQueueDefinition
                {
                    Brand = PrinterBrand.Epson,
                    DisplayName = "Good",
                    PrinterHostAddress = "10.0.0.2",
                    PortNumber = 9100,
                    Protocol = TcpPrinterProtocol.Raw
                }
            },
            DomainCredential = new NetworkCredential("u", "p")
        };

        var sut = new PrinterDeploymentOrchestrator(m.Object);
        var events = new List<DeploymentProgressEvent>();
        await sut.RunAsync(request, new InlineProgress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e is { PrinterQueueName: "Bad", State: TargetMachineState.Error });
        Assert.Contains(events, e => e is { PrinterQueueName: "Good", State: TargetMachineState.CompletedSuccess });
    }
}
