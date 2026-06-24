namespace PrinterInstall.Core.Remote;

public sealed class RemoteHostSession
{
    public RemoteHostSession(string host, bool requiresElevatedExecution)
    {
        Host = host;
        RequiresElevatedExecution = requiresElevatedExecution;
        PreflightCompleted = true;
    }

    public string Host { get; }
    public bool RequiresElevatedExecution { get; private set; }
    public bool PreflightCompleted { get; }

    public void MarkRequiresElevatedExecution() => RequiresElevatedExecution = true;
}
