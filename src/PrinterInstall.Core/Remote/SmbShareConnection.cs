using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Mounts a Windows SMB share (e.g. \\host\ADMIN$) with explicit credentials and releases it on dispose.
/// </summary>
public sealed class SmbShareConnection : IDisposable
{
    private const int ResourceTypeDisk = 0x00000001;

    private readonly string _remoteName;
    private bool _disposed;

    private SmbShareConnection(string remoteName)
    {
        _remoteName = remoteName;
    }

    /// <summary>
    /// Opens a connection to \\host\shareName (e.g. shareName = ADMIN$).
    /// </summary>
    public static SmbShareConnection Open(string host, string shareName, NetworkCredential credential)
    {
        var remote = $@"\\{host.Trim()}\{shareName.Trim('\\', '/')}";
        var netResource = new NetResource
        {
            ResourceType = ResourceTypeDisk,
            RemoteName = remote
        };
        var user = string.IsNullOrEmpty(credential.Domain)
            ? credential.UserName
            : $"{credential.Domain}\\{credential.UserName}";
        var code = WNetAddConnection2(netResource, credential.Password ?? "", user, 0);
        if (code != 0)
            throw new Win32Exception(code, $"SMB mount of {remote} failed (Win32 error {code}).");
        return new SmbShareConnection(remote);
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
