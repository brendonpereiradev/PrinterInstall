using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class WmiRemoteProcessRunner : IRemoteProcessRunner, IRemoteWmiProcessRunner
{
    public Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(host, credential);
            return WmiProcessRunnerCore.Run(scope, commandLine, timeout, cancellationToken);
        }, cancellationToken);
    }

    private static ManagementScope CreateScope(string host, NetworkCredential credential)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Username = string.IsNullOrEmpty(credential.Domain)
                ? credential.UserName
                : $"{credential.Domain}\\{credential.UserName}",
            Password = credential.Password ?? "",
            EnablePrivileges = true
        };
        return new ManagementScope($@"\\{host.Trim()}\root\cimv2", options);
    }
}
