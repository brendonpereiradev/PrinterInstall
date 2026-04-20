namespace PrinterInstall.Core.Remote;

/// <summary>
/// UNC and local-on-target paths for a temporary driver staging folder under
/// C:\Windows\Temp\PrinterInstall\&lt;id&gt;\ on the remote machine.
/// </summary>
public sealed record RemoteDriverStagingPaths(
    string StagingId,
    string UncRoot,
    string LocalOnTargetRoot)
{
    public static RemoteDriverStagingPaths Create(string host)
    {
        var id = Guid.NewGuid().ToString("N");
        var h = host.Trim();
        return new RemoteDriverStagingPaths(
            id,
            $@"\\{h}\ADMIN$\Temp\PrinterInstall\{id}",
            $@"C:\Windows\Temp\PrinterInstall\{id}");
    }

    public string UncInfPath(string infFileName) => Path.Combine(UncRoot, infFileName);

    public string LocalInfPath(string infFileName) => Path.Combine(LocalOnTargetRoot, infFileName);

    public string UncLogPath(string logName) => Path.Combine(UncRoot, logName);

    public string LocalLogPath(string logName) => Path.Combine(LocalOnTargetRoot, logName);
}
