using System.Net;
using Moq;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelValidationTests
{
    private static MainViewModel CreateSut(SessionContext session, IConfirmationDialogService? dialogService = null)
    {
        var identity = new LocalMachineIdentity();
        var remoteMock = new Mock<IRemotePrinterOperations>();
        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "EPSON Universal Print Driver" });
        remoteMock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new PrinterDeploymentOrchestrator(remoteMock.Object);
        var rollbackRunner = new DeploymentRollbackRunner(remoteMock.Object, new PrinterControlOrchestrator(remoteMock.Object));

        return new MainViewModel(
            session,
            orchestrator,
            rollbackRunner,
            null!,
            identity,
            dialogService: dialogService);
    }

    [Fact]
    public async Task DeployAsync_InvertedDisplayNameAndHost_WhenUserCancelsDialog_BlocksAndLogsCancellation()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        mockDialog.Setup(d => d.ConfirmInversionCorrectionAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(false);

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "10.1.152.218";
        sut.PrinterRows[0].PrinterHostAddress = "Multifuncional";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmInversionCorrectionAsync(It.Is<IReadOnlyList<string>>(list => list.Count == 1)), Times.Once);
        Assert.Contains("cancelada", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("10.1.152.218", sut.PrinterRows[0].DisplayName);
        Assert.Equal("Multifuncional", sut.PrinterRows[0].PrinterHostAddress);
        Assert.Empty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_InvertedDisplayNameAndHost_WhenUserAcceptsDialog_InvertsFieldsAndContinuesDeploy()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        mockDialog.Setup(d => d.ConfirmInversionCorrectionAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(true);

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "10.1.152.218";
        sut.PrinterRows[0].PrinterHostAddress = "Multifuncional";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmInversionCorrectionAsync(It.Is<IReadOnlyList<string>>(list => list.Count == 1)), Times.Once);
        Assert.Equal("Multifuncional", sut.PrinterRows[0].DisplayName);
        Assert.Equal("10.1.152.218", sut.PrinterRows[0].PrinterHostAddress);
        Assert.Contains("Inversão corrigida automaticamente", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_MultipleInvertedRows_WhenUserAccepts_InvertsAllRowsAndLogsCorrections()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        mockDialog.Setup(d => d.ConfirmInversionCorrectionAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(true);

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "10.1.152.218";
        sut.PrinterRows[0].PrinterHostAddress = "Multifuncional";

        sut.AddPrinterRowCommand.Execute(null);
        sut.PrinterRows[1].Brand = PrinterBrand.Lexmark;
        sut.PrinterRows[1].DisplayName = "10.1.152.219";
        sut.PrinterRows[1].PrinterHostAddress = "Lexmark_Office";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmInversionCorrectionAsync(It.Is<IReadOnlyList<string>>(list => list.Count == 2)), Times.Once);
        Assert.Equal("Multifuncional", sut.PrinterRows[0].DisplayName);
        Assert.Equal("10.1.152.218", sut.PrinterRows[0].PrinterHostAddress);
        Assert.Equal("Lexmark_Office", sut.PrinterRows[1].DisplayName);
        Assert.Equal("10.1.152.219", sut.PrinterRows[1].PrinterHostAddress);
        Assert.Contains("Nome definido como 'Multifuncional' e IP como '10.1.152.218'", sut.LogText);
        Assert.Contains("Nome definido como 'Lexmark_Office' e IP como '10.1.152.219'", sut.LogText);
        Assert.NotEmpty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_InvalidHostAddressWithSpacesAndAccents_BlocksAndLogsInvalidHost()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var sut = CreateSut(session);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Multifuncional";
        sut.PrinterRows[0].PrinterHostAddress = "Consultório 6";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Contains("inválido", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_SuspiciousLabelMismatch_WhenUserRejectsDialog_AbortsDeployAndLogsCancellation()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        mockDialog.Setup(d => d.ConfirmDeployWarningAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(false); // Usuário escolhe Cancelar

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "ETIQ_TRIAGEM";
        sut.PrinterRows[0].PrinterHostAddress = "10.1.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmDeployWarningAsync(It.Is<IReadOnlyList<string>>(w => w.Count == 1)), Times.Once);
        Assert.Contains("cancelada", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_SuspiciousOfficeMismatchOnGainscha_WhenUserRejectsDialog_AbortsDeploy()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        mockDialog.Setup(d => d.ConfirmDeployWarningAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(false);

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Gainscha;
        sut.PrinterRows[0].GainschaLabelPreset = GainschaLabelPreset.Paciente;
        sut.PrinterRows[0].DisplayName = "LASER_ADM";
        sut.PrinterRows[0].PrinterHostAddress = "10.1.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmDeployWarningAsync(It.Is<IReadOnlyList<string>>(w => w.Count == 1)), Times.Once);
        Assert.Contains("cancelada", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_ConsistentQueues_DoesNotInvokeConfirmationDialog()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();

        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "PRINTER_RECEPCAO";
        sut.PrinterRows[0].PrinterHostAddress = "10.1.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ConfirmDeployWarningAsync(It.IsAny<IReadOnlyList<string>>()), Times.Never);
        Assert.NotEmpty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_WhenComputersEmpty_InvokesNoComputersWarningDialogAndLogsWarning()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var mockDialog = new Mock<IConfirmationDialogService>();
        var sut = CreateSut(session, mockDialog.Object);

        sut.ComputersText = "   ";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Epson_Recepcao";
        sut.PrinterRows[0].PrinterHostAddress = "10.1.1.50";

        await sut.DeployCommand.ExecuteAsync(null);

        mockDialog.Verify(d => d.ShowNoComputersWarningAsync(), Times.Once);
        Assert.Contains("Informe pelo menos um nome de computador", sut.LogText);
        Assert.Empty(sut.Targets);
    }
}

