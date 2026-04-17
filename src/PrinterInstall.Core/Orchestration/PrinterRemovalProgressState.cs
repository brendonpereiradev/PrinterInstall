namespace PrinterInstall.Core.Orchestration;

public enum PrinterRemovalProgressState
{
    ContactingRemote,
    RemovingQueue,
    RemovingOrphanPort,
    TargetCompleted,
    Warning,
    Error
}
