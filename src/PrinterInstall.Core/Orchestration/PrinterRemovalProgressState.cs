namespace PrinterInstall.Core.Orchestration;

public enum PrinterRemovalProgressState
{
    ContactingRemote,
    RenamingQueue,
    RemovingQueue,
    RemovingOrphanPort,
    TargetCompleted,
    Warning,
    Error
}
