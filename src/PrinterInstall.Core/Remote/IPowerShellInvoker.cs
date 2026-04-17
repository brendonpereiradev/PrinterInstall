using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IPowerShellInvoker
{
    Task<IReadOnlyList<string>> InvokeOnRemoteRunspaceAsync(string computerName, NetworkCredential credential, string innerScript, CancellationToken cancellationToken = default);
}
