namespace PrinterInstall.Core.Orchestration;

public enum PrinterRemovalProgressState
{
    ContactingRemote,
    RenamingQueue,
    RemovingQueue,
    RemovingOrphanPort,
    RollbackSucceeded,
    TargetCompleted,
    Warning,
    Error
}
