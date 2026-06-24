using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Detecta se um nome de computador na lista de alvos corresponde à máquina local.
/// </summary>
public sealed class LocalMachineIdentity
{
    private readonly object _lock = new();
    private HashSet<string>? _localNames;

    public bool IsLocalMachine(string computerName)
    {
        if (string.IsNullOrWhiteSpace(computerName))
            return false;

        return GetLocalNames().Contains(computerName.Trim());
    }

    /// <summary>
    /// Hostname curto preferido para inserir na lista de alvos da UI.
    /// </summary>
    public string GetPrimaryLocalName() => Environment.MachineName;

    private HashSet<string> GetLocalNames()
    {
        if (_localNames is not null)
            return _localNames;

        lock (_lock)
        {
            return _localNames ??= BuildLocalNames();
        }
    }

    private static HashSet<string> BuildLocalNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                names.Add(value.Trim());
        }

        Add(Environment.MachineName);
        Add("localhost");
        Add(".");
        Add("127.0.0.1");
        Add("::1");

        try
        {
            Add(Dns.GetHostName());
        }
        catch
        {
            // Best effort.
        }

        try
        {
            var hostEntry = Dns.GetHostEntry(Environment.MachineName);
            Add(hostEntry.HostName);
            foreach (var alias in hostEntry.Aliases)
                Add(alias);
        }
        catch
        {
            // Best effort.
        }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                        Add(address.Address.ToString());
                }
            }
        }
        catch
        {
            // Best effort.
        }

        return names;
    }
}
