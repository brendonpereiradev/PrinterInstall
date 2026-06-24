using System.Management;
using System.Net;
using System.Text;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Operações de impressora via WMI local (<c>root\cimv2</c>) e processos in-process.
/// </summary>
public sealed class LocalPrinterOperations : IRemotePrinterOperations
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RenameOperationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PrintTestPageTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SpoolerSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PrintJobWaitTimeout = TimeSpan.FromSeconds(10);

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            var query = new ObjectQuery("SELECT Name FROM Win32_PrinterDriver");
            using var searcher = new ManagementObjectSearcher(scope, query);

            var list = new List<string>();
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var raw = mo["Name"]?.ToString();
                    var normalized = WmiPrinterOperationsCore.NormalizeWmiDriverName(raw);
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
            var scope = WmiPrinterOperationsCore.CreateLocalScope();

            if (WmiPrinterOperationsCore.PortExists(scope, portName))
                return;

            using var portClass = new ManagementClass(scope, new ManagementPath("Win32_TCPIPPrinterPort"), null);
            using var port = portClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_TCPIPPrinterPort instance.");

            port["Name"] = portName;
            port["HostAddress"] = printerHostAddress;
            port["PortNumber"] = portNumber;
            port["Protocol"] = WmiPrinterOperationsCore.MapProtocol(protocol);
            port["SNMPEnabled"] = false;
            port["Queue"] = "";

            port.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken);
    }

    public Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            return WmiPrinterOperationsCore.PrinterExists(scope, printerDisplayName);
        }, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();

            if (WmiPrinterOperationsCore.PrinterExists(scope, printerName))
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

    public async Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SpoolerSettleDelay, cancellationToken).ConfigureAwait(false);

        var errors = new List<string>();

        if (await TryWmiPrintTestPageAsync(printerQueueName, cancellationToken).ConfigureAwait(false))
            return;
        errors.Add("WMI PrintTestPage did not queue a spooler job.");

        if (await TryProcessPrintTestPageAsync(
                WmiPrinterOperationsCore.BuildPrintTestPageRundll32CommandLine(printerQueueName),
                "printui",
                printerQueueName,
                cancellationToken).ConfigureAwait(false))
            return;
        errors.Add("printui PrintUIEntry /k did not queue a spooler job.");

        if (await TryProcessPrintTestPageAsync(
                WmiPrinterOperationsCore.BuildPrintTestPageCommandLine(printerQueueName),
                "Print-TestPage",
                printerQueueName,
                cancellationToken).ConfigureAwait(false))
            return;
        errors.Add("Print-TestPage did not queue a spooler job.");

        throw new InvalidOperationException(
            $"Print test page failed for '{printerQueueName}': {string.Join(" | ", errors)}");
    }

    private static async Task<bool> TryWmiPrintTestPageAsync(string printerQueueName, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                var scope = WmiPrinterOperationsCore.CreateLocalScope();
                WmiPrinterOperationsCore.PrintTestPageOnScope(scope, printerQueueName, cancellationToken);
                return WmiPrinterOperationsCore.WaitForPrintJobOnPrinter(
                    scope, printerQueueName, PrintJobWaitTimeout, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryProcessPrintTestPageAsync(
        string commandLine,
        string methodLabel,
        string printerQueueName,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await LocalProcessRunner.RunWithOutputAsync(commandLine, PrintTestPageTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (output.Result.TimedOut || output.Result.ReturnValue != 0)
                return false;

            return await Task.Run(() =>
            {
                var scope = WmiPrinterOperationsCore.CreateLocalScope();
                return WmiPrinterOperationsCore.WaitForPrintJobOnPrinter(
                    scope, printerQueueName, PrintJobWaitTimeout, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RemotePrinterQueueInfo>>(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            var query = new ObjectQuery("SELECT Name, PortName FROM Win32_Printer");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var list = new List<RemotePrinterQueueInfo>();
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (mo)
                {
                    var name = mo["Name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    var port = mo["PortName"]?.ToString();
                    list.Add(new RemotePrinterQueueInfo(name, port));
                }
            }
            return list;
        }, cancellationToken);
    }

    public Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{WmiPrinterOperationsCore.EscapeWql(printerName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    mo.Delete();
            }
        }, cancellationToken);
    }

    public async Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
    {
        var cmd = WmiPrinterOperationsCore.BuildRenamePrinterCommandLine(currentName, newName);
        var runResult = await LocalProcessRunner.RunAsync(cmd, RenameOperationTimeout, cancellationToken).ConfigureAwait(false);
        if (runResult.TimedOut)
            throw new TimeoutException($"Renomear a fila localmente excedeu o tempo de {RenameOperationTimeout}.");
        if (runResult.ReturnValue != 0)
            throw new InvalidOperationException($"Renomear a fila localmente falhou (exit code {runResult.ReturnValue}).");
    }

    public Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            var query = new ObjectQuery($"SELECT Name FROM Win32_Printer WHERE PortName='{WmiPrinterOperationsCore.EscapeWql(portName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var count = 0;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo) { count++; }
            }
            return count;
        }, cancellationToken);
    }

    public Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            var query = new ObjectQuery($"SELECT * FROM Win32_TCPIPPrinterPort WHERE Name='{WmiPrinterOperationsCore.EscapeWql(portName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    mo.Delete();
            }
        }, cancellationToken);
    }

    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "PrinterInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            log?.Report("Staging driver files locally...");
            CopyDirectory(package.RootFolder, stagingRoot, cancellationToken);

            var infLocal = Path.Combine(stagingRoot, package.InfFileName);
            var installLogLocal = Path.Combine(stagingRoot, "install.log");
            var installScriptLocal = Path.Combine(stagingRoot, "install.ps1");

            var scriptContent = WmiPrinterOperationsCore.BuildInstallerScript(infLocal, package.ExpectedDriverName, installLogLocal);
            File.WriteAllText(installScriptLocal, scriptContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var runCmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{installScriptLocal}\"";
            log?.Report($"Launching install script locally (timeout {InstallTimeout.TotalMinutes:F0}min)...");

            var runResult = await LocalProcessRunner.RunAsync(runCmd, InstallTimeout, cancellationToken).ConfigureAwait(false);
            var installOutput = File.Exists(installLogLocal) ? File.ReadAllText(installLogLocal) : string.Empty;

            foreach (var line in WmiPrinterOperationsCore.SplitLines(installOutput))
                log?.Report(line);

            if (runResult.TimedOut)
                throw new TimeoutException($"Install script timed out locally after {InstallTimeout}.");

            if (runResult.ProcessId is null && runResult.ReturnValue != 0)
            {
                var startFailure = WmiPrinterOperationsCore.DescribeInstallScriptFailure(installOutput, runResult.ReturnValue, runResult.ReturnValue);
                throw new InvalidOperationException(startFailure);
            }

            var resultLine = WmiPrinterOperationsCore.ExtractResultLine(installOutput);
            if (runResult.ReturnValue != 0 || !string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
            {
                var detail = WmiPrinterOperationsCore.DescribeInstallScriptFailure(
                    installOutput,
                    runResult.ReturnValue,
                    runResult.ProcessId is null ? runResult.ReturnValue : null);
                throw new InvalidOperationException($"Add-PrinterDriver failed locally: {detail}");
            }
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
