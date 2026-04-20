using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed record RemoteProcessResult(uint ReturnValue, uint? ProcessId, bool TimedOut);

public interface IRemoteProcessRunner
{
    Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken);
}
