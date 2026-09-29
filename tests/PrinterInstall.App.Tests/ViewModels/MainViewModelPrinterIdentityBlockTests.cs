using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelPrinterIdentityBlockTests
{
    [Fact]
    public async Task DeployAsync_WhenDetectedBrandDiffers_ShowsStyledBlockingDialog()
    {
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var identity = new Mock<IPrinterIdentityService>();
        identity.Setup(s => s.IdentifyAsync("10.1.152.216", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrinterIdentityResult("10.1.152.216", PrinterBrand.Lexmark, "CX532ADWE", "SNMP"));
        var dialogs = new Mock<IConfirmationDialogService>();
        dialogs.Setup(d => d.ShowPrinterIdentityBlockAsync(It.IsAny<IReadOnlyList<string>>(), true))
            .Returns(Task.CompletedTask);
        var orchestrator = new PrinterDeploymentOrchestrator(
            remote.Object,
            new NullLocalDriverPackageCatalog(),
            new DirectRawPrinterTestService(identity.Object),
            new NullFastHostReachabilityChecker(),
            new NullPrinterPingService(),
            TransientRetryHelper.DefaultMaxAttempts,
            TransientRetryHelper.DefaultInitialDelay,
            identityService: identity.Object);
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var vm = new MainViewModel(
            session,
            orchestrator,
            new DeploymentRollbackRunner(remote.Object, new PrinterControlOrchestrator(remote.Object)),
            null!,
            new LocalMachineIdentity(),
            dialogService: dialogs.Object,
            notificationService: new Mock<IDeploymentNotificationService>().Object);
        vm.ComputersText = "pc-01";
        vm.PrinterRows[0].Brand = PrinterBrand.Epson;
        vm.PrinterRows[0].DisplayName = "Recepcao";
        vm.PrinterRows[0].PrinterHostAddress = "10.1.152.216";

        await vm.DeployCommand.ExecuteAsync(null);

        dialogs.Verify(d => d.ShowPrinterIdentityBlockAsync(It.Is<IReadOnlyList<string>>(reasons =>
            reasons.Count == 1 && reasons[0].Contains("Lexmark") && reasons[0].Contains("Epson")), true), Times.Once);
        var target = Assert.Single(vm.Targets);
        Assert.Equal(TargetMachineState.PrinterIdentityMismatch, target.State);
        Assert.Equal("Lexmark ≠ Epson", target.Message);
        Assert.Contains("implantação bloqueada", vm.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("via SNMP", vm.LogText);
        Assert.DoesNotContain('—', vm.LogText);
        remote.VerifyNoOtherCalls();
    }
}
