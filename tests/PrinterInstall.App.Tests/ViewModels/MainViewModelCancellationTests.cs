using System.Diagnostics;
using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Logging;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelCancellationTests
{
    [Fact]
    public async Task CancelDuringDiagnostics_MarksWaitingTargetsAsCancelled_WithoutStartingDeploy()
    {
        var collecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = new Mock<ILocalDiagnosticCollector>();
        collector.Setup(x => x.CollectAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                collecting.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "";
            });
        var remote = new Mock<IRemotePrinterOperations>();
        var session = new SessionContext { Credential = new NetworkCredential("admin", "unique-secret", "DOMAIN") };
        var rollback = new DeploymentRollbackRunner(remote.Object, new PrinterControlOrchestrator(remote.Object));
        var vm = new MainViewModel(session, TestOrchestratorFactory.Create(remote.Object), rollback, null!,
            new LocalMachineIdentity(), notificationService: new Mock<IDeploymentNotificationService>().Object,
            diagnosticCollector: collector.Object);
        vm.ComputersText = "target-pc";
        vm.PrinterRows[0].Brand = PrinterBrand.Epson;
        vm.PrinterRows[0].DisplayName = "Recepção";
        vm.PrinterRows[0].PrinterHostAddress = "192.0.2.10";

        var running = vm.DeployCommand.ExecuteAsync(null);
        await collecting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(vm.Targets, t => Assert.Equal(TargetMachineState.Pending, t.State));
        vm.CancelDeployCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(vm.IsDeployRunning);
        Assert.All(vm.Targets, t => Assert.Equal(TargetMachineState.DeployCancelled, t.State));
        remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancel_RespondsWhileNativeReadBlocks_ThenStopsWithoutStartingMutations()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(x => x.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.TrySetResult();
                // Reproduz uma chamada nativa síncrona sem suporte a CancellationToken.
                release.Wait(TimeSpan.FromSeconds(3));
                throw new UnauthorizedAccessException("native access denied");
            });
        var session = new SessionContext { Credential = new NetworkCredential("admin", "unique-secret", "DOMAIN") };
        var rollback = new DeploymentRollbackRunner(remote.Object, new PrinterControlOrchestrator(remote.Object));
        var export = new Mock<ILogExportService>();
        export.Setup(x => x.ExportLog(It.IsAny<string>(), It.IsAny<string>())).Returns(LogExportResult.Cancelled());
        var vm = new MainViewModel(session, TestOrchestratorFactory.Create(remote.Object), rollback, null!,
            new LocalMachineIdentity(), export.Object, new Mock<IDeploymentNotificationService>().Object);
        vm.ComputersText = "target-pc";
        vm.PrinterRows[0].Brand = PrinterBrand.Epson;
        vm.PrinterRows[0].DisplayName = "Recepção";
        vm.PrinterRows[0].PrinterHostAddress = "192.0.2.10";

        var watch = Stopwatch.StartNew();
        var running = vm.DeployCommand.ExecuteAsync(null);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "A chamada nativa bloqueou o chamador da UI.");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(vm.IsDeployRunning);
            vm.CancelDeployCommand.Execute(null);
            Assert.True(vm.IsCancellationRequested);
            Assert.Equal("Cancelando…", vm.CancelDeployButtonText);
            Assert.False(vm.CancelDeployCommand.CanExecute(null));
            Assert.Contains("Cancelamento solicitado pelo operador", vm.LogText);
            var firstClickLog = vm.LogText;
            vm.CancelDeployCommand.Execute(null);
            Assert.Equal(firstClickLog, vm.LogText);
            Assert.True(vm.ExportLogCommand.CanExecute(null));
            vm.ExportLogCommand.Execute(null);
            export.Verify(x => x.ExportLog(It.IsAny<string>(), It.Is<string>(s => s.Contains("Usuário do processo") && s.Contains("caminho: REMOTO"))), Times.Once);
        }
        finally { release.Set(); }
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsDeployRunning);
        Assert.All(vm.Targets, t => Assert.Equal(TargetMachineState.DeployCancelled, t.State));
        remote.Verify(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        remote.Verify(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
