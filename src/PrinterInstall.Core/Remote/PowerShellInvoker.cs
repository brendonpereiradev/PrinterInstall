using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;

namespace PrinterInstall.Core.Remote;

public sealed class PowerShellInvoker : IPowerShellInvoker
{
    public async Task<IReadOnlyList<string>> InvokeOnRemoteRunspaceAsync(string computerName, NetworkCredential credential, string innerScript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(computerName))
            throw new ArgumentException("Computer name is required.", nameof(computerName));

        var secure = ToSecureString(credential.Password);
        var userName = BuildUserName(credential);
        var psCred = new PSCredential(userName, secure);

        var uri = new Uri($"http://{computerName.Trim()}:5985/wsman");
        var connInfo = new WSManConnectionInfo(uri, "http://schemas.microsoft.com/powershell/Microsoft.PowerShell", psCred);

        using var runspace = RunspaceFactory.CreateRunspace(connInfo);
        await Task.Run(() => runspace.Open(), cancellationToken).ConfigureAwait(false);

        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddScript(innerScript);

        var output = await Task.Run(() => ps.Invoke(), cancellationToken).ConfigureAwait(false);

        if (ps.HadErrors)
        {
            var err = string.Join("; ", ps.Streams.Error.ReadAll().Select(e => e.ToString()));
            throw new InvalidOperationException($"Remote PowerShell failed: {err}");
        }

        return output.Select(o => o?.ToString() ?? string.Empty).Where(s => s.Length > 0).ToList();
    }

    private static string BuildUserName(NetworkCredential credential)
    {
        if (!string.IsNullOrEmpty(credential.Domain))
            return $"{credential.Domain}\\{credential.UserName}";
        return credential.UserName;
    }

    private static SecureString ToSecureString(string? password)
    {
        var s = new SecureString();
        if (password != null)
        {
            foreach (var c in password)
                s.AppendChar(c);
        }
        s.MakeReadOnly();
        return s;
    }
}
