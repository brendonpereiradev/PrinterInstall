using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardAdvanceTests
{
    private static (RemovalWizardViewModel Sut, Mock<IRemotePrinterOperations> RemoteMock) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        var orchestrator = new PrinterControlOrchestrator(remoteMock.Object);

        var sut = new RemovalWizardViewModel(
            session,
            remoteMock.Object,
            orchestrator);

        return (sut, remoteMock);
    }

    [Fact]
    public async Task CanAdvance_TrueOnStep1_EvenIfNoPrintersSelected()
    {
        var (sut, remoteMock) = CreateSut();
        remoteMock.Setup(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RemotePrinterQueueInfo("Consultório 4", "10.5.104.66"),
                new RemotePrinterQueueInfo("Consultório 4 (USB)", "USB005")
            });

        sut.ComputersText = "NOTE-001\nNOTE-002";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.CurrentStepIndex);
        Assert.Equal("NOTE-001", sut.CurrentComputerName);
        Assert.Equal(2, sut.QueuesForCurrentComputer.Count);
        Assert.False(sut.QueuesForCurrentComputer[0].IsSelected);
        Assert.False(sut.QueuesForCurrentComputer[1].IsSelected);

        // Mesmo sem marcar nada, deve permitir avançar
        Assert.True(sut.CanAdvanceQueueStep);
        Assert.True(sut.NextQueueStepCommand.CanExecute(null));
    }

    [Fact]
    public async Task NextQueueStepCommand_AdvancesToNextComputer_WithoutSelection()
    {
        var (sut, remoteMock) = CreateSut();
        remoteMock.Setup(m => m.ListPrinterQueuesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila", "IP_1.1.1.1") });

        sut.ComputersText = "NOTE-001\nNOTE-002";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.Equal("NOTE-001", sut.CurrentComputerName);

        // Clica em Avançar sem selecionar nada
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.CurrentStepIndex);
        Assert.Equal("NOTE-002", sut.CurrentComputerName);
    }

    [Fact]
    public async Task NextQueueStepCommand_OnLastComputerWithoutSelection_NavigatesToReview()
    {
        var (sut, remoteMock) = CreateSut();
        remoteMock.Setup(m => m.ListPrinterQueuesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila1", "IP_1.1.1.1") });

        sut.ComputersText = "NOTE-001\nNOTE-002";
        await sut.StartCommand.ExecuteAsync(null);

        // No primeiro computador, avança sem selecionar
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        Assert.Equal("NOTE-002", sut.CurrentComputerName);

        // No segundo computador, avança sem selecionar nada
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        // Deve ter chegado ao Step 2 (Revisão)
        Assert.Equal(2, sut.CurrentStepIndex);

        // Ambas as máquinas devem constar como "(nenhuma ação)"
        Assert.Contains("NOTE-001: (nenhuma ação)", sut.ReviewSummary);
        Assert.Contains("NOTE-002: (nenhuma ação)", sut.ReviewSummary);
    }

    [Fact]
    public async Task AdvanceCommand_DisabledWhenBusy()
    {
        var (sut, remoteMock) = CreateSut();
        remoteMock.Setup(m => m.ListPrinterQueuesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila1", "IP_1.1.1.1") });

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null);

        Assert.True(sut.CanAdvanceQueueStep);

        sut.IsLoadingQueues = true;
        Assert.False(sut.CanAdvanceQueueStep);
        Assert.False(sut.NextQueueStepCommand.CanExecute(null));

        sut.IsLoadingQueues = false;
        Assert.True(sut.CanAdvanceQueueStep);

        sut.IsResettingSpooler = true;
        Assert.False(sut.CanAdvanceQueueStep);
        Assert.False(sut.NextQueueStepCommand.CanExecute(null));
    }
}
