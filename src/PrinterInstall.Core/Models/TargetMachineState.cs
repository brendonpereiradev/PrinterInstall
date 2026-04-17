namespace PrinterInstall.Core.Models;

public enum TargetMachineState
{
    Pending,
    ContactingRemote,
    ValidatingDriver,
    Configuring,
    CompletedSuccess,
    AbortedDriverMissing,
    Error
}
