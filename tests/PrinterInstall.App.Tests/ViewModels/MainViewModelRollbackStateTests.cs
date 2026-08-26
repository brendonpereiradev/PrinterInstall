using System.Net;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using Xunit;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelRollbackStateTests
{
    [Fact]
    public async Task DeployAsync_CancelledDuringConfiguration_RollsBackAndResolvesTargetRowStates()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "domain"),
            DomainName = "domain"
        };
        var remoteMock = new Mock<IRemotePrinterOperations>();
        remoteMock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Gainscha GA-2408T" });
        remoteMock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        remoteMock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MainViewModel? vm = null;
        remoteMock.Setup(m => m.ConfigureGainschaLabelPresetAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<GainschaLabelPreset>(), It.IsAny<CancellationToken>()))
            .Returns<string, NetworkCredential, string, GainschaLabelPreset, CancellationToken>((_, _, _, _, ct) =>
            {
                vm?.CancelDeployCommand.Execute(null);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        remoteMock.Setup(m => m.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(m => m.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remoteMock.Setup(m => m.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new PrinterDeploymentOrchestrator(remoteMock.Object);
        var rollbackRunner = new DeploymentRollbackRunner(remoteMock.Object, new PrinterControlOrchestrator(remoteMock.Object));
        var identity = new LocalMachineIdentity();

        vm = new MainViewModel(
            session,
            orchestrator,
            rollbackRunner,
            null!,
            identity);

        vm.ComputersText = "pc-01";
        vm.PrinterRows[0].Brand = PrinterBrand.Gainscha;
        vm.PrinterRows[0].DisplayName = "Etiquetadora";
        vm.PrinterRows[0].PrinterHostAddress = "10.0.0.30";
        vm.PrinterRows[0].GainschaLabelPreset = GainschaLabelPreset.Paciente;

        await vm.DeployCommand.ExecuteAsync(null);

        Assert.Single(vm.Targets);
        var target = vm.Targets[0];
        Assert.True(
            target.State is TargetMachineState.RolledBack or TargetMachineState.DeployCancelled,
            $"Expected RolledBack or DeployCancelled, but was {target.State}");
    }
}
