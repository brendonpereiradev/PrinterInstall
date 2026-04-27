using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Orchestration;

public sealed record DeploymentProgressEvent(
    string ComputerName,
    TargetMachineState State,
    string Message,
    string? PrinterQueueName = null);
