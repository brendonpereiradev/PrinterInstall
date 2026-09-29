using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.TestSupport;

// Os testes existentes de implantação exercitam o comportamento de driver, fila e rollback. A pré-validação de identidade
// possui testes dedicados e é desativada apenas para essas instâncias de teste legadas.
public static class TestDeploymentOrchestratorFactory
{
    private static PrinterDeploymentOrchestrator Legacy(PrinterDeploymentOrchestrator orchestrator)
    {
        orchestrator.SkipIdentityValidationForLegacyTests();
        return orchestrator;
    }

    public static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote) =>
        Legacy(new PrinterDeploymentOrchestrator(remote));

    public static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers) =>
        Legacy(new PrinterDeploymentOrchestrator(remote, localDrivers));

    public static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService) =>
        Legacy(new PrinterDeploymentOrchestrator(remote, localDrivers, rawTestService));

    public static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers,
        int maxRetryAttempts, TimeSpan retryDelay) =>
        Legacy(new PrinterDeploymentOrchestrator(remote, localDrivers, maxRetryAttempts, retryDelay));

    public static PrinterDeploymentOrchestrator Create(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService, IFastHostReachabilityChecker reachabilityChecker,
        IPrinterPingService printerPingService, int maxRetryAttempts, TimeSpan retryDelay,
        int? configuredMaxDegreeOfParallelism = null) =>
        Legacy(new PrinterDeploymentOrchestrator(remote, localDrivers, rawTestService, reachabilityChecker,
            printerPingService, maxRetryAttempts, retryDelay, configuredMaxDegreeOfParallelism));
}
