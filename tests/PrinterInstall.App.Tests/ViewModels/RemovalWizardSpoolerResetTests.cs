using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardSpoolerResetTests
{
    private static (RemovalWizardViewModel Sut, Mock<IRemotePrinterOperations> RemoteMock, Mock<IConfirmationDialogService> DialogMock) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        var orchestrator = new PrinterControlOrchestrator(remoteMock.Object);
        var dialogMock = new Mock<IConfirmationDialogService>();

        var sut = new RemovalWizardViewModel(
            session,
            remoteMock.Object,
            orchestrator,
            dialogService: dialogMock.Object);

        return (sut, remoteMock, dialogMock);
    }

    [Fact]
    public async Task ResetCurrentMachineSpoolerAsync_UserDeclinesConfirmDialog_DoesNotCallReset()
    {
        var (sut, remoteMock, dialogMock) = CreateSut();
        dialogMock.Setup(d => d.ConfirmSpoolerResetAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null); // Avança para Step 1

        Assert.Equal(1, sut.CurrentStepIndex);
        Assert.True(sut.CanResetSpooler);

        await sut.ResetCurrentMachineSpoolerCommand.ExecuteAsync(null);

        remoteMock.Verify(m => m.ResetSpoolerServiceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetCurrentMachineSpoolerAsync_UserConfirmsAndSucceeds_CallsResetAndReloadsMachineQueues()
    {
        var (sut, remoteMock, dialogMock) = CreateSut();
        dialogMock.Setup(d => d.ConfirmSpoolerResetAsync("NOTE-001"))
            .ReturnsAsync(true);

        remoteMock.Setup(m => m.ResetSpoolerServiceAsync("NOTE-001", It.IsAny<NetworkCredential>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpoolerResetResult.Success("Spooler reiniciado."));

        remoteMock.Setup(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RemotePrinterQueueInfo("Fila1", "IP_10.1.1.1") });

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null);

        await sut.ResetCurrentMachineSpoolerCommand.ExecuteAsync(null);

        remoteMock.Verify(m => m.ResetSpoolerServiceAsync("NOTE-001", It.IsAny<NetworkCredential>(), true, It.IsAny<CancellationToken>()), Times.Once);
        // ListPrinterQueuesAsync deve ser chamado no StartCommand e recarregado após o reset
        remoteMock.Verify(m => m.ListPrinterQueuesAsync("NOTE-001", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Contains("Spooler reiniciado e fila limpa com sucesso", sut.LogText);
    }

    [Fact]
    public async Task ResetCurrentMachineSpoolerAsync_RemoteFails_LogsErrorAndDoesNotThrow()
    {
        var (sut, remoteMock, dialogMock) = CreateSut();
        dialogMock.Setup(d => d.ConfirmSpoolerResetAsync("NOTE-001"))
            .ReturnsAsync(true);

        remoteMock.Setup(m => m.ResetSpoolerServiceAsync("NOTE-001", It.IsAny<NetworkCredential>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpoolerResetResult.Failure("Acesso negado"));

        sut.ComputersText = "NOTE-001";
        await sut.StartCommand.ExecuteAsync(null);

        await sut.ResetCurrentMachineSpoolerCommand.ExecuteAsync(null);

        Assert.Contains("Falha ao reiniciar Spooler", sut.LogText);
        Assert.Contains("Acesso negado", sut.LogText);
    }

    [Fact]
    public async Task CanResetSpooler_OnlyTrueWhenStep1AndNotBusy()
    {
        var (sut, _, _) = CreateSut();
        sut.ComputersText = "NOTE-001";

        // Step 0
        Assert.False(sut.CanResetSpooler);

        // Step 1
        await sut.StartCommand.ExecuteAsync(null);
        Assert.True(sut.CanResetSpooler);

        // Simulando estado ocupado
        sut.IsResettingSpooler = true;
        Assert.False(sut.CanResetSpooler);

        sut.IsResettingSpooler = false;
        Assert.True(sut.CanResetSpooler);
    }
}
