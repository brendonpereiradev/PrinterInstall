using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterDeploymentOrchestrator
{
    private readonly IRemotePrinterOperations _remote;
    private readonly ILocalDriverPackageCatalog _localDrivers;

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote)
        : this(remote, new NullLocalDriverPackageCatalog())
    {
    }

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers)
    {
        _remote = remote;
        _localDrivers = localDrivers;
    }

    public async Task RunAsync(PrinterDeploymentRequest request, IProgress<DeploymentProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var computer in request.TargetComputerNames)
        {
            try
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ContactingRemote, "Connecting..."));
                var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ValidatingDriver, $"Checking driver (found {drivers.Count})..."));
                var expected = PrinterCatalog.GetExpectedDriverName(request.Brand);

                if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
                {
                    if (!await TryInstallMissingDriverAsync(computer, request, expected, progress, cancellationToken).ConfigureAwait(false))
                        continue;
                }

                var portName = BuildPortName(request.PrinterHostAddress, request.PortNumber);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Creating port..."));
                var protocol = MapProtocol(request.Protocol);
                await _remote.CreateTcpPrinterPortAsync(computer, request.DomainCredential, portName, request.PrinterHostAddress, request.PortNumber, protocol, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Adding printer..."));
                await _remote.AddPrinterAsync(computer, request.DomainCredential, request.DisplayName, expected, portName, cancellationToken).ConfigureAwait(false);
                if (request.PrintTestPage)
                {
                    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Sending test page..."));
                    try
                    {
                        await _remote.PrintTestPageAsync(computer, request.DomainCredential, request.DisplayName, cancellationToken).ConfigureAwait(false);
                        progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, "Done"));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, $"Done — test page failed: {Flatten(ex)}"));
                    }
                }
                else
                {
                    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, "Done"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, Flatten(ex)));
            }
        }
    }

    private async Task<bool> TryInstallMissingDriverAsync(
        string computer,
        PrinterDeploymentRequest request,
        string expected,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        var package = _localDrivers.TryGet(request.Brand);
        if (package is null)
        {
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver not installed: {expected}. No local package available."));
            return false;
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.InstallingDriver,
            $"Installing driver package '{package.InfFileName}' on {computer}..."));

        var log = new Progress<string>(msg =>
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.InstallingDriver, msg)));

        try
        {
            await _remote.InstallPrinterDriverAsync(computer, request.DomainCredential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver not installed: {expected}. install unsupported on this channel."));
            return false;
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.DriverInstalledReconfirming,
            "Revalidating driver after install..."));

        var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
        if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
        {
            var sample = string.Join(" | ", drivers.Take(10));
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver installed does not match expected. Expected: {expected}. Found: [{sample}]"));
            return false;
        }

        return true;
    }

    private static string BuildPortName(string printerHostAddress, int portNumber)
    {
        var host = printerHostAddress.Trim();
        return portNumber == 9100 ? host : $"{host}_{portNumber}";
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message?.Trim();
            if (!string.IsNullOrEmpty(msg))
                messages.Add(msg);
        }
        return string.Join(" | ", messages);
    }

    private static string MapProtocol(TcpPrinterProtocol p) => p switch
    {
        TcpPrinterProtocol.Raw => "RAW",
        TcpPrinterProtocol.Lpr => "LPR",
        TcpPrinterProtocol.Ipp => "IPP",
        _ => "RAW"
    };
}
