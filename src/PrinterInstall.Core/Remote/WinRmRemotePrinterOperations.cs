using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class WinRmRemotePrinterOperations : IRemotePrinterOperations
{
    private readonly IPowerShellInvoker _invoker;

    public WinRmRemotePrinterOperations(IPowerShellInvoker invoker)
    {
        _invoker = invoker;
    }

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        const string inner = "Get-PrinterDriver | Select-Object -ExpandProperty Name";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-PrinterPort -Name '{Escape(portName)}' -PrinterHostAddress '{Escape(printerHostAddress)}' -PortNumber {portNumber}";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-Printer -Name '{Escape(printerName)}' -DriverName '{Escape(driverName)}' -PortName '{Escape(portName)}'";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
