namespace PrinterInstall.Core.Models;

public enum TargetMachineState
{
    Pending,
    IdentifyingPrinter,
    ValidatingPrinter,
    PrinterIdentityMismatch,
    PrinterIdentityUnknown,
    ContactingRemote,
    ValidatingDriver,
    InstallingDriver,
    DriverInstalledReconfirming,
    Configuring,
    CompletedSuccess,
    SkippedAlreadyExists,
    AbortedDriverMissing,
    Error,
    DeployCancelled,
    RollbackRemovingQueue,
    RollbackRemovingPort,
    RolledBack
}
