using System.Globalization;
using System.Net;
using PrinterInstall.Core.Models;

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

    public async Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        const string inner = """
$printers = Get-Printer | Select-Object Name, PortName
@($printers) | ConvertTo-Json -Compress -Depth 4
""";
        var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken).ConfigureAwait(false);
        var json = result.Count > 0 ? result[0] : string.Empty;
        var parsed = RemotePrinterQueueInfoJsonParser.Parse(json);
        return parsed.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
    }

    public Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$p = Get-Printer -Name '{Escape(printerName)}' -ErrorAction SilentlyContinue
if ($null -ne $p) {{ Remove-Printer -Name '{Escape(printerName)}' -Confirm:$false }}
";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public async Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$c = @(Get-Printer | Where-Object {{ $_.PortName -eq '{Escape(portName)}' }}).Count
$c.ToString()
";
        var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken).ConfigureAwait(false);
        if (result.Count == 0)
            return 0;
        return int.Parse(result[^1].Trim(), CultureInfo.InvariantCulture);
    }

    public Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$port = Get-PrinterPort -Name '{Escape(portName)}' -ErrorAction SilentlyContinue
if ($null -ne $port) {{ Remove-PrinterPort -Name '{Escape(portName)}' -Confirm:$false }}
";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
