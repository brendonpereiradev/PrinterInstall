using System.Globalization;

namespace PrinterInstall.Core.Remote;

public static class RemoteElevatedScriptBuilder
{
    public static string WrapWithResultHandling(string innerScriptBody) =>
$@"$ErrorActionPreference = 'Stop'
try {{
{innerScriptBody}
    Write-Output 'RESULT>> OK'
    exit 0
}} catch {{
    Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)
    exit 1
}}";

    public static string BuildCreateTcpPortScript(string portName, string hostAddress, int portNumber, string protocol)
    {
        var port = EscapePs(portName);
        var host = EscapePs(hostAddress);
        var body = protocol.Equals("LPR", StringComparison.OrdinalIgnoreCase)
            ? $@"
    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} -PortMonitor 'LPR Port Monitor' | Out-Null"
            : $@"
    if (Get-PrinterPort -Name '{port}' -ErrorAction SilentlyContinue) {{ return }}
    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildAddPrinterScript(string printerName, string driverName, string portName)
    {
        var n = EscapePs(printerName);
        var d = EscapePs(driverName);
        var p = EscapePs(portName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    if (Get-Printer -Name '{n}' -ErrorAction SilentlyContinue) {{ return }}
    Add-Printer -Name '{n}' -DriverName '{d}' -PortName '{p}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRemovePrinterScript(string printerName)
    {
        var n = EscapePs(printerName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    Remove-Printer -Name '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRemoveTcpPortScript(string portName)
    {
        var p = EscapePs(portName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    if (Get-PrinterPort -Name '{p}' -ErrorAction SilentlyContinue) {{
        Remove-PrinterPort -Name '{p}' -ErrorAction Stop | Out-Null
    }}";
        return WrapWithResultHandling(body);
    }

    public static string BuildPrintTestPageScript(string printerQueueName)
    {
        var n = EscapePs(printerQueueName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    Print-TestPage -PrinterName '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRenamePrinterScript(string currentName, string newName)
    {
        var c = EscapePs(currentName);
        var n = EscapePs(newName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    $null = Get-Printer -Name '{c}' -ErrorAction Stop
    Rename-Printer -Name '{c}' -NewName '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    private static string EscapePs(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
