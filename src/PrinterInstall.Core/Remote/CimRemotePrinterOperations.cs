using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Implementa operações remotas via WMI/DCOM (fallback para quando WinRM não está disponível).
/// Lista drivers instalados, cria portas TCP/IP e adiciona filas de impressão.
/// </summary>
public sealed class CimRemotePrinterOperations : IRemotePrinterOperations
{
    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            var query = new ObjectQuery("SELECT Name FROM Win32_PrinterDriver");
            using var searcher = new ManagementObjectSearcher(scope, query);

            var list = new List<string>();
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var raw = mo["Name"]?.ToString();
                    var normalized = NormalizeWmiDriverName(raw);
                    if (!string.IsNullOrEmpty(normalized))
                        list.Add(normalized);
                }
                finally
                {
                    mo.Dispose();
                }
            }

            return (IReadOnlyList<string>)list;
        }, cancellationToken);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            if (PortExists(scope, portName))
                return;

            using var portClass = new ManagementClass(scope, new ManagementPath("Win32_TCPIPPrinterPort"), null);
            using var port = portClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_TCPIPPrinterPort instance.");

            port["Name"] = portName;
            port["HostAddress"] = printerHostAddress;
            port["PortNumber"] = portNumber;
            port["Protocol"] = MapProtocol(protocol);
            port["SNMPEnabled"] = false;
            port["Queue"] = "";

            port.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            if (PrinterExists(scope, printerName))
                return;

            using var printerClass = new ManagementClass(scope, new ManagementPath("Win32_Printer"), null);
            using var printer = printerClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_Printer instance.");

            printer["DeviceID"] = printerName;
            printer["Name"] = printerName;
            printer["DriverName"] = driverName;
            printer["PortName"] = portName;
            printer["Network"] = true;
            printer["Shared"] = false;

            printer.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken);
    }

    private static ManagementScope CreateScope(string computerName, NetworkCredential credential)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Username = BuildCredentialUserName(credential),
            Password = credential.Password ?? "",
            EnablePrivileges = true
        };
        var path = $@"\\{computerName.Trim()}\root\cimv2";
        return new ManagementScope(path, options);
    }

    private static bool PortExists(ManagementScope scope, string portName)
    {
        var query = new ObjectQuery($"SELECT Name FROM Win32_TCPIPPrinterPort WHERE Name='{EscapeWql(portName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static bool PrinterExists(ManagementScope scope, string printerName)
    {
        var query = new ObjectQuery($"SELECT Name FROM Win32_Printer WHERE Name='{EscapeWql(printerName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static int MapProtocol(string protocol)
    {
        if (string.Equals(protocol, "LPR", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    /// <summary>
    /// Win32_PrinterDriver.Name vem no formato "Driver,Version,Environment"
    /// (por exemplo, "Lexmark Universal v4 XL,3,Windows x64"). Mantemos só o driver.
    /// </summary>
    private static string NormalizeWmiDriverName(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var commaIndex = raw.IndexOf(',');
        return (commaIndex >= 0 ? raw[..commaIndex] : raw).Trim();
    }

    private static string BuildCredentialUserName(NetworkCredential credential)
    {
        if (!string.IsNullOrEmpty(credential.Domain))
            return $"{credential.Domain}\\{credential.UserName}";
        return credential.UserName;
    }

    private static string EscapeWql(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
}
