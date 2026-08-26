using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using Xunit;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardViewModelNotificationTests
{
    private class FakeNotificationService : IDeploymentNotificationService
    {
        public int SuccessCallCount { get; private set; }
        public int WarningCallCount { get; private set; }
        public int ErrorCallCount { get; private set; }

        public void NotifySuccess()
        {
            SuccessCallCount++;
        }

        public void NotifyWarning()
        {
            WarningCallCount++;
        }

        public void NotifyError()
        {
            ErrorCallCount++;
        }
    }

    private static (RemovalWizardViewModel Sut, FakeNotificationService FakeNotification, Mock<IRemotePrinterOperations> RemoteMock) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        var orchestrator = new PrinterControlOrchestrator(remoteMock.Object);
        var fakeNotification = new FakeNotificationService();

        var vm = new RemovalWizardViewModel(
            session,
            remoteMock.Object,
            orchestrator,
            null,
            null,
            fakeNotification);

        return (vm, fakeNotification, remoteMock);
    }

    [Fact]
    public async Task ExecuteAsync_AllRemovalsSucceed_TriggersNotifySuccess()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("pc-01", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemotePrinterQueueInfo>
            {
                new("OldPrinter", "IP_1.1.1.1")
            });
        remoteMock.Setup(m => m.RemovePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "OldPrinter", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.CountPrintersUsingPortAsync("pc-01", It.IsAny<NetworkCredential>(), "IP_1.1.1.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remoteMock.Setup(m => m.RemoveTcpPrinterPortAsync("pc-01", It.IsAny<NetworkCredential>(), "IP_1.1.1.1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        sut.ComputersText = "pc-01";
        await sut.StartCommand.ExecuteAsync(null);

        sut.QueuesForCurrentComputer[0].IsSelected = true;
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        await sut.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(1, fakeNotification.SuccessCallCount);
        Assert.Equal(0, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_AllRemovalsFail_TriggersNotifyError()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("pc-01", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemotePrinterQueueInfo>
            {
                new("OldPrinter", "IP_1.1.1.1")
            });
        remoteMock.Setup(m => m.RemovePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "OldPrinter", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Falha de acesso WMI"));

        sut.ComputersText = "pc-01";
        await sut.StartCommand.ExecuteAsync(null);

        sut.QueuesForCurrentComputer[0].IsSelected = true;
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        await sut.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(0, fakeNotification.WarningCallCount);
        Assert.Equal(1, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_MixedSuccessAndError_TriggersNotifyWarning()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("pc-01", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemotePrinterQueueInfo>
            {
                new("Queue1", "IP_1.1.1.1"),
                new("Queue2", "IP_1.1.1.2")
            });
        remoteMock.Setup(m => m.RemovePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "Queue1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.CountPrintersUsingPortAsync("pc-01", It.IsAny<NetworkCredential>(), "IP_1.1.1.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1); // Port still in use
        remoteMock.Setup(m => m.RemovePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "Queue2", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Erro ao remover fila 2"));

        sut.ComputersText = "pc-01";
        await sut.StartCommand.ExecuteAsync(null);

        sut.QueuesForCurrentComputer[0].IsSelected = true;
        sut.QueuesForCurrentComputer[1].IsSelected = true;
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        await sut.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(1, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PortWarning_TriggersNotifyWarning()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("pc-01", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemotePrinterQueueInfo>
            {
                new("OldPrinter", "IP_1.1.1.1")
            });
        remoteMock.Setup(m => m.RemovePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "OldPrinter", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.CountPrintersUsingPortAsync("pc-01", It.IsAny<NetworkCredential>(), "IP_1.1.1.1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Erro ao verificar portas"));

        sut.ComputersText = "pc-01";
        await sut.StartCommand.ExecuteAsync(null);

        sut.QueuesForCurrentComputer[0].IsSelected = true;
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        await sut.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(1, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RenamesSucceed_TriggersNotifySuccess()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("pc-01", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemotePrinterQueueInfo>
            {
                new("OldQueue", "IP_1.1.1.1")
            });
        remoteMock.Setup(m => m.RenamePrinterQueueAsync("pc-01", It.IsAny<NetworkCredential>(), "OldQueue", "NewQueue", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        sut.ComputersText = "pc-01";
        await sut.StartCommand.ExecuteAsync(null);

        sut.QueuesForCurrentComputer[0].NewName = "NewQueue";
        await sut.NextQueueStepCommand.ExecuteAsync(null);

        await sut.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(1, fakeNotification.SuccessCallCount);
        Assert.Equal(0, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }
}
