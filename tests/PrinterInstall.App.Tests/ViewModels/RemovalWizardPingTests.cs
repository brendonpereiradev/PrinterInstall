using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardPingTests
{
    private static (
        RemovalWizardViewModel Sut,
        Mock<IRemotePrinterOperations> RemoteMock,
        Mock<IPrinterPingService> PingMock,
        Mock<IFastHostReachabilityChecker> ReachabilityMock) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        var orchestrator = new PrinterControlOrchestrator(remoteMock.Object);
        var pingMock = new Mock<IPrinterPingService>();
        var reachabilityMock = new Mock<IFastHostReachabilityChecker>();

        var sut = new RemovalWizardViewModel(
            session,
            remoteMock.Object,
            orchestrator,
            pingService: pingMock.Object,
            reachabilityChecker: reachabilityMock.Object);

        return (sut, remoteMock, pingMock, reachabilityMock);
    }

    [Fact]
    public async Task LoadCurrentMachineAsync_WhenPingSucceeds_MarksOnlineAndLoadsQueues()
    {
        var (sut, remoteMock, pingMock, reachabilityMock) = CreateSut();

        pingMock.Setup(p => p.PingAsync("NOTE-001", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RemotePrinterQueueInfo("Fila-01", "10.0.0.1"),
                new RemotePrinterQueueInfo("Fila-02", "10.0.0.2")
            });

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.CurrentStepIndex);
        Assert.Equal(ComputerPingStatus.Online, sut.PingStatus);
        Assert.False(sut.IsComputerOffline);
        Assert.True(sut.HasPingBadge);
        Assert.Equal(2, sut.QueuesForCurrentComputer.Count);
        Assert.Null(sut.QueuesLoadError);

        remoteMock.Verify(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadCurrentMachineAsync_WhenPingAndReachabilityFail_MarksOfflineAndBypassesWmi()
    {
        var (sut, remoteMock, pingMock, reachabilityMock) = CreateSut();

        pingMock.Setup(p => p.PingAsync("NOTE-OFFLINE", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        reachabilityMock.Setup(r => r.CheckReachabilityAsync("NOTE-OFFLINE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Host inacessível nas portas RPC/SMB."));

        sut.ComputersText = "NOTE-OFFLINE";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.CurrentStepIndex);
        Assert.Equal(ComputerPingStatus.Offline, sut.PingStatus);
        Assert.True(sut.IsComputerOffline);
        Assert.True(sut.ShowComputerOfflineHint);
        Assert.False(sut.ShowQueuesEmptyHint);
        Assert.NotNull(sut.QueuesLoadError);
        Assert.Contains("não respondeu ao ping", sut.QueuesLoadError);
        Assert.Empty(sut.QueuesForCurrentComputer);

        // Não deve tentar chamar WMI (evitando travamento de 60s)
        remoteMock.Verify(m => m.ListPrinterQueuesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Never);

        // Comandos de navegação devem estar disponíveis
        Assert.True(sut.CanAdvanceQueueStep);
        Assert.True(sut.CanRetryCurrentMachine);
        Assert.False(sut.CanResetSpooler); // Spooler reset não deve ser permitido em máquina offline
    }

    [Fact]
    public async Task LoadCurrentMachineAsync_WhenPingFailsButRpcReachabilitySucceeds_MarksOnlineAndLoadsQueues()
    {
        var (sut, remoteMock, pingMock, reachabilityMock) = CreateSut();

        // ICMP bloqueado por firewall
        pingMock.Setup(p => p.PingAsync("NOTE-FIREWALLED", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Portas RPC/SMB abertas
        reachabilityMock.Setup(r => r.CheckReachabilityAsync("NOTE-FIREWALLED", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("NOTE-FIREWALLED", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila-OK", "10.0.0.1") });

        sut.ComputersText = "NOTE-FIREWALLED";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal(ComputerPingStatus.Online, sut.PingStatus);
        Assert.False(sut.IsComputerOffline);
        Assert.Single(sut.QueuesForCurrentComputer);
        remoteMock.Verify(m => m.ListPrinterQueuesAsync("NOTE-FIREWALLED", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryCurrentMachineCommand_WhenMachineTurnsOn_ReloadsQueuesSuccessfully()
    {
        var (sut, remoteMock, pingMock, reachabilityMock) = CreateSut();

        var isMachineOn = false;
        pingMock.Setup(p => p.PingAsync("NOTE-001", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => isMachineOn);
        reachabilityMock.Setup(r => r.CheckReachabilityAsync("NOTE-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (isMachineOn, isMachineOn ? null : "offline"));

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila-01", "10.0.0.1") });

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.True(sut.IsComputerOffline);
        Assert.Equal(ComputerPingStatus.Offline, sut.PingStatus);

        // Operador liga o PC e clica em Testar Novamente
        isMachineOn = true;
        Assert.True(sut.CanRetryCurrentMachine);
        await sut.RetryCurrentMachineCommand.ExecuteAsync(null);

        Assert.False(sut.IsComputerOffline);
        Assert.Equal(ComputerPingStatus.Online, sut.PingStatus);
        Assert.Single(sut.QueuesForCurrentComputer);
        remoteMock.Verify(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CanAdvanceQueueStep_ImmediatelyAvailableWhenMachineOffline()
    {
        var (sut, _, pingMock, reachabilityMock) = CreateSut();
        pingMock.Setup(p => p.PingAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        reachabilityMock.Setup(r => r.CheckReachabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "offline"));

        sut.ComputersText = "NOTE-OFFLINE";
        await sut.StartCommand.ExecuteAsync(null);

        // Como a máquina está offline, o carregamento encerra imediatamente sem travar
        Assert.False(sut.IsLoadingQueues);
        Assert.True(sut.IsComputerOffline);
        Assert.True(sut.CanAdvanceQueueStep);
        Assert.True(sut.NextQueueStepCommand.CanExecute(null));
    }
}
