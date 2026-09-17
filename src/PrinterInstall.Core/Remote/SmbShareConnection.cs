using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Conecta a um compartilhamento SMB do Windows (ex: \\host\ADMIN$ ou \\host\IPC$) com credenciais explícitas e libera no dispose.
/// </summary>
public sealed class SmbShareConnection : IDisposable
{
    private const int ResourceTypeAny = 0x00000000;
    private const int ResourceTypeDisk = 0x00000001;

    private const int ErrorAlreadyAssigned = 85;
    private const int ErrorDeviceAlreadyRemembered = 1202;
    private const int ErrorSessionCredentialConflict = 1219;

    private readonly string _remoteName;
    private bool _disposed;

    private SmbShareConnection(string remoteName)
    {
        _remoteName = remoteName;
    }

    /// <summary>
    /// Abre uma conexão para \\host\shareName (ex: shareName = ADMIN$ ou IPC$).
    /// </summary>
    public static SmbShareConnection Open(string host, string shareName, NetworkCredential credential)
    {
        var cleanHost = host.Trim();
        var cleanShare = shareName.Trim('\\', '/');
        var remote = $@"\\{cleanHost}\{cleanShare}";
        var isIpc = cleanShare.Equals("IPC$", StringComparison.OrdinalIgnoreCase);

        var netResource = new NetResource
        {
            ResourceType = isIpc ? ResourceTypeAny : ResourceTypeDisk,
            RemoteName = remote
        };
        var user = CredentialHelper.BuildCredentialUserName(credential);
        var password = credential.Password ?? "";

        var code = WNetAddConnection2(netResource, password, user, 0);

        if (code is ErrorSessionCredentialConflict or ErrorAlreadyAssigned or ErrorDeviceAlreadyRemembered)
        {
            PurgeHostConnections(cleanHost, remote);
            Thread.Sleep(100);

            code = WNetAddConnection2(netResource, password, user, 0);

            if (code == ErrorSessionCredentialConflict && IsResourceAccessible(cleanHost, cleanShare))
            {
                return new SmbShareConnection(remote);
            }
        }

        if (code != 0 && code != ErrorAlreadyAssigned)
            throw new Win32Exception(code, $"SMB mount of {remote} failed (Win32 error {code}).");

        return new SmbShareConnection(remote);
    }

    private static void PurgeHostConnections(string host, string remote)
    {
        _ = WNetCancelConnection2(remote, 0, true);
        _ = WNetCancelConnection2($@"\\{host}\IPC$", 0, true);
        _ = WNetCancelConnection2($@"\\{host}\ADMIN$", 0, true);
        _ = WNetCancelConnection2($@"\\{host}", 0, true);
    }

    private static bool IsResourceAccessible(string host, string share)
    {
        try
        {
            if (share.Equals("IPC$", StringComparison.OrdinalIgnoreCase))
                return true;

            var unc = $@"\\{host}\{share}";
            return Directory.Exists(unc);
        }
        catch
        {
            return false;
        }
    }

    public string RemoteRoot => _remoteName;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = WNetCancelConnection2(_remoteName, 0, true);
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int ResourceType;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
