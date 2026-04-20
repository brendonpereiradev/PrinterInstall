using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemoteDriverFileStager
{
    Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken);

    Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken);

    Task CleanupAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a text file (e.g. an installer PowerShell script) into the already
    /// staged UNC folder on the remote host. Default no-op for legacy callers.
    /// </summary>
    Task WriteTextFileAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string fileName, string content, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
