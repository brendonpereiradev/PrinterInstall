using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests;

internal static class TestOrchestratorFactory
{
    internal static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote)
    {
        var orchestrator = new PrinterDeploymentOrchestrator(remote);
        orchestrator.SkipIdentityValidationForLegacyTests();
        return orchestrator;
    }
}
