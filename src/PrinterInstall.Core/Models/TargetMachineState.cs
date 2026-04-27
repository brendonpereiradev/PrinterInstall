namespace PrinterInstall.Core.Models;

public enum TargetMachineState
{
    Pending,
    ContactingRemote,
    ValidatingDriver,
    InstallingDriver,
    DriverInstalledReconfirming,
    Configuring,
    CompletedSuccess,
    SkippedAlreadyExists,
    AbortedDriverMissing,
    Error
}
