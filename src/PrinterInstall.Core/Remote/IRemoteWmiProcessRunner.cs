using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemoteWmiProcessRunner
{
    Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken);
}
