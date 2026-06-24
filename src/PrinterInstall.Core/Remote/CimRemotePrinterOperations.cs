using System.Management;
using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Operações remotas via WMI/DCOM (<c>\\host\root\cimv2</c>).
/// Lista drivers, cria portas TCP/IP, gere filas, remove/renomeia e instala drivers (SMB + Win32_Process).
/// </summary>
public sealed class CimRemotePrinterOperations : IRemotePrinterOperations
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RenameOperationTimeout = TimeSpan.FromMinutes(2);

    private readonly IRemoteDriverFileStager _stager;
    private readonly RemoteHostSessionFactory _sessionFactory;
    private readonly IRemoteWmiProcessRunner _processRunner;
    private readonly ElevatedRemoteProcessRunner _elevatedRunner;

    public CimRemotePrinterOperations(
        IRemoteDriverFileStager stager,
        RemoteHostSessionFactory sessionFactory,
        IRemoteWmiProcessRunner processRunner,
        ElevatedRemoteProcessRunner elevatedRunner)
    {
        _stager = stager;
        _sessionFactory = sessionFactory;
        _processRunner = processRunner;
        _elevatedRunner = elevatedRunner;
    }

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = ConnectRemote(computerName, credential);
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
        return ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: () => Task.Run(() =>
            {
                var scope = ConnectRemote(computerName, credential);

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
            }, cancellationToken),
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildCreateTcpPortScript(portName, printerHostAddress, portNumber, protocol);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, InstallTimeout, null, cancellationToken);
            });
    }

    public Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = ConnectRemote(computerName, credential);
            return WmiPrinterOperationsCore.PrinterExists(scope, printerDisplayName);
        }, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: () => Task.Run(() =>
            {
                var scope = ConnectRemote(computerName, credential);

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
            }, cancellationToken),
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildAddPrinterScript(printerName, driverName, portName);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, InstallTimeout, null, cancellationToken);
            });
    }

    public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: () => Task.Run(() =>
            {
                var scope = ConnectRemote(computerName, credential);
                var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{WmiPrinterOperationsCore.EscapeWql(printerQueueName)}'");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject mo in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (mo)
                    {
                        WmiPrinterOperationsCore.InvokeWmiPrintTestPage(mo);
                        return;
                    }
                }

                throw new InvalidOperationException($"Printer queue not found for test page: {printerQueueName}");
            }, cancellationToken),
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildPrintTestPageScript(printerQueueName);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, InstallTimeout, null, cancellationToken);
            });
    }

    public Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RemotePrinterQueueInfo>>(() =>
        {
            var scope = ConnectRemote(computerName, credential);
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
        return ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: () => Task.Run(() =>
            {
                var scope = ConnectRemote(computerName, credential);
                var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{WmiPrinterOperationsCore.EscapeWql(printerName)}'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                        mo.Delete();
                }
            }, cancellationToken),
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildRemovePrinterScript(printerName);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, InstallTimeout, null, cancellationToken);
            });
    }

    public async Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
    {
        await ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: async () =>
            {
                var cmd = WmiPrinterOperationsCore.BuildRenamePrinterCommandLine(currentName, newName);
                var runResult = await _processRunner.RunAsync(computerName, credential, cmd, RenameOperationTimeout, cancellationToken).ConfigureAwait(false);
                if (runResult.TimedOut)
                    throw new TimeoutException($"Renomear a fila em {computerName} excedeu o tempo de {RenameOperationTimeout}.");
                if (runResult.ReturnValue != 0)
                    throw new InvalidOperationException($"Renomear a fila em {computerName} falhou (WMI return {runResult.ReturnValue}).");
            },
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildRenamePrinterScript(currentName, newName);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, RenameOperationTimeout, null, cancellationToken);
            }).ConfigureAwait(false);
    }

    public Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = ConnectRemote(computerName, credential);
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
        return ExecuteMutationAsync(
            computerName,
            credential,
            log: null,
            cancellationToken,
            direct: () => Task.Run(() =>
            {
                var scope = ConnectRemote(computerName, credential);
                var query = new ObjectQuery($"SELECT * FROM Win32_TCPIPPrinterPort WHERE Name='{WmiPrinterOperationsCore.EscapeWql(portName)}'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                        mo.Delete();
                }
            }, cancellationToken),
            elevated: () =>
            {
                var script = RemoteElevatedScriptBuilder.BuildRemoveTcpPortScript(portName);
                return _elevatedRunner.RunElevatedScriptAsync(computerName, credential, script, InstallTimeout, null, cancellationToken);
            });
    }

    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        var session = await _sessionFactory.PrepareAsync(computerName, credential, log, cancellationToken).ConfigureAwait(false);

        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(StageTimeout);

        RemoteDriverStagingPaths paths;
        try
        {
            log?.Report($"Staging driver files on \\\\{computerName}\\ADMIN$...");
            paths = await _stager.StageAsync(computerName, credential, package.RootFolder, stageCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to stage driver files to {computerName}: {ex.Message}", ex);
        }

        try
        {
            var infLocal = paths.LocalInfPath(package.InfFileName);
            var installLogLocal = paths.LocalLogPath("install.log");
            var installScriptLocal = paths.LocalInfPath("install.ps1");
            var runElevated = session.RequiresElevatedExecution;
            var scriptContent = WmiPrinterOperationsCore.BuildInstallerScript(
                infLocal,
                package.ExpectedDriverName,
                installLogLocal,
                skipRunAsBlock: runElevated);

            async Task ReadAndReportInstallLogAsync()
            {
                var installOutput = await _stager.ReadLogAsync(computerName, credential, paths, "install.log", cancellationToken).ConfigureAwait(false);
                foreach (var line in WmiPrinterOperationsCore.SplitLines(installOutput))
                    log?.Report(line);
            }

            if (!runElevated)
            {
                try
                {
                    await _stager.WriteTextFileAsync(computerName, credential, paths, "install.ps1", scriptContent, cancellationToken).ConfigureAwait(false);
                    var runCmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{installScriptLocal}\"";

                    log?.Report($"Launching install script on {computerName} via WMI (timeout {InstallTimeout.TotalMinutes:F0}min)...");
                    var runResult = await _processRunner.RunAsync(computerName, credential, runCmd, InstallTimeout, cancellationToken).ConfigureAwait(false);
                    await ReadAndReportInstallLogAsync().ConfigureAwait(false);

                    if (runResult.ReturnValue != 0)
                        throw new InvalidOperationException($"Install script could not start on {computerName} (WMI return {runResult.ReturnValue}).");
                    if (runResult.TimedOut)
                        throw new TimeoutException($"Install script timed out on {computerName} after {InstallTimeout}. Remote process was killed.");

                    var installOutput = await _stager.ReadLogAsync(computerName, credential, paths, "install.log", cancellationToken).ConfigureAwait(false);
                    var resultLine = WmiPrinterOperationsCore.ExtractResultLine(installOutput);
                    if (!string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
                    {
                        var detail = string.IsNullOrEmpty(resultLine) ? "no RESULT line" : resultLine;
                        throw new InvalidOperationException($"Add-PrinterDriver failed on {computerName}: {detail}");
                    }

                    return;
                }
                catch (Exception ex) when (AccessDeniedDetector.IsAccessDenied(ex))
                {
                    session.MarkRequiresElevatedExecution();
                    log?.Report($"Token administrativo filtrado detectado em {computerName} — execução elevada temporária");
                    runElevated = true;
                    scriptContent = WmiPrinterOperationsCore.BuildInstallerScript(
                        infLocal,
                        package.ExpectedDriverName,
                        installLogLocal,
                        skipRunAsBlock: true);
                }
            }

            if (runElevated)
            {
                await _elevatedRunner.RunElevatedScriptAsync(
                    computerName,
                    credential,
                    scriptContent,
                    InstallTimeout,
                    log,
                    cancellationToken).ConfigureAwait(false);
                await ReadAndReportInstallLogAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await _stager.CleanupAsync(computerName, credential, paths, CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal async Task ExecuteMutationAsync(
        string computerName,
        NetworkCredential credential,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        Func<Task> direct,
        Func<Task> elevated)
    {
        var session = await _sessionFactory.PrepareAsync(computerName, credential, log, cancellationToken).ConfigureAwait(false);

        async Task RunElevatedAsync()
        {
            await elevated().ConfigureAwait(false);
        }

        if (session.RequiresElevatedExecution)
        {
            await RunElevatedAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await direct().ConfigureAwait(false);
        }
        catch (Exception ex) when (AccessDeniedDetector.IsAccessDenied(ex))
        {
            session.MarkRequiresElevatedExecution();
            log?.Report($"Token administrativo filtrado detectado em {computerName} — execução elevada temporária");
            await RunElevatedAsync().ConfigureAwait(false);
        }
    }

    private static ManagementScope ConnectRemote(string computerName, NetworkCredential credential)
    {
        var scope = WmiPrinterOperationsCore.CreateRemoteScope(computerName, credential);
        scope.Connect();
        return scope;
    }
}
