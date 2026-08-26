using System;
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

public class MainViewModelNotificationTests
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

    private static (MainViewModel Sut, FakeNotificationService FakeNotification, Mock<IRemotePrinterOperations> RemoteMock) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        var orchestrator = new PrinterDeploymentOrchestrator(remoteMock.Object);
        var rollbackRunner = new DeploymentRollbackRunner(remoteMock.Object, new PrinterControlOrchestrator(remoteMock.Object));
        var identity = new LocalMachineIdentity();
        var fakeNotification = new FakeNotificationService();

        var vm = new MainViewModel(
            session,
            orchestrator,
            rollbackRunner,
            null!,
            identity,
            null,
            fakeNotification);

        return (vm, fakeNotification, remoteMock);
    }

    [Fact]
    public async Task DeployAsync_AllTargetsSucceed_TriggersNotifySuccess()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "EPSON Universal Print Driver" });
        remoteMock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        sut.ComputersText = "pc-01";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Epson_Recepcao";
        sut.PrinterRows[0].PrinterHostAddress = "192.168.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Equal(1, fakeNotification.SuccessCallCount);
        Assert.Equal(0, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task DeployAsync_AllTargetsFail_TriggersNotifyError()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Falha de conexão WMI"));

        sut.ComputersText = "pc-01";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Epson_Recepcao";
        sut.PrinterRows[0].PrinterHostAddress = "192.168.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(0, fakeNotification.WarningCallCount);
        Assert.Equal(1, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task DeployAsync_MixedSuccessAndError_TriggersNotifyWarning()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync("pc-ok", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "EPSON Universal Print Driver" });
        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync("pc-err", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Host inacessível"));
        remoteMock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        sut.ComputersText = "pc-ok\r\npc-err";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Epson_Recepcao";
        sut.PrinterRows[0].PrinterHostAddress = "192.168.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(1, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }

    [Fact]
    public async Task DeployAsync_Cancelled_TriggersNotifyWarning()
    {
        var (sut, fakeNotification, remoteMock) = CreateSut();

        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        sut.ComputersText = "pc-01";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Epson_Recepcao";
        sut.PrinterRows[0].PrinterHostAddress = "192.168.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeNotification.SuccessCallCount);
        Assert.Equal(1, fakeNotification.WarningCallCount);
        Assert.Equal(0, fakeNotification.ErrorCallCount);
    }
}
