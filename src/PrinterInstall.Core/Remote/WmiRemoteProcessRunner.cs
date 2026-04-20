using System.Globalization;
using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class WmiRemoteProcessRunner : IRemoteProcessRunner
{
    public Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(host, credential);
            scope.Connect();

            using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
            using var inParams = processClass.GetMethodParameters("Create");
            inParams["CommandLine"] = commandLine;

            using var outParams = processClass.InvokeMethod("Create", inParams, null);
            var returnValue = Convert.ToUInt32(outParams["ReturnValue"], CultureInfo.InvariantCulture);
            if (returnValue != 0)
                return new RemoteProcessResult(returnValue, null, TimedOut: false);

            var pid = Convert.ToUInt32(outParams["ProcessId"], CultureInfo.InvariantCulture);
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // The user asked to cancel: kill the remote process so it
                    // doesn't keep running on the target for the full timeout.
                    TryTerminate(scope, pid);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (!ProcessExists(scope, pid))
                    return new RemoteProcessResult(0, pid, TimedOut: false);
                Thread.Sleep(500);
            }

            TryTerminate(scope, pid);
            return new RemoteProcessResult(0, pid, TimedOut: true);
        }, cancellationToken);
    }

    private static bool ProcessExists(ManagementScope scope, uint pid)
    {
        var query = new ObjectQuery($"SELECT ProcessId FROM Win32_Process WHERE ProcessId = {pid}");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static void TryTerminate(ManagementScope scope, uint pid)
    {
        try
        {
            var query = new ObjectQuery($"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    mo.InvokeMethod("Terminate", new object[] { 1u });
            }
        }
        catch
        {
            // Best effort.
        }
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
