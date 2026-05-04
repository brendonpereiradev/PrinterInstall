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

    public async Task RunAsync(
        PrinterDeploymentRequest request,
        DeploymentRollbackJournal rollbackJournal,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken = default)
    {
        foreach (var computer in request.TargetComputerNames)
        {
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ContactingRemote, "Connecting...", null));

            IReadOnlyList<string> drivers;
            try
            {
                drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, Flatten(ex), null));
                continue;
            }

            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ValidatingDriver, $"Checking driver (found {drivers.Count})...", null));

            var brandOrder = DistinctBrandsInOrder(request.Printers);
            var failedBrands = new HashSet<PrinterBrand>();
            var brandFailureMessage = new Dictionary<PrinterBrand, string>();

            foreach (var brand in brandOrder)
            {
                var driverOrder = PrinterCatalog.GetDriverResolutionOrder(brand);
                if (DriverNameMatcher.IsAnyAcceptedDriverInstalled(drivers, driverOrder))
                    continue;

                try
                {
                    var (ok, errorDetail) = await TryInstallMissingDriverAsync(computer, request, brand, progress, cancellationToken)
                        .ConfigureAwait(false);
                    if (!ok)
                    {
                        failedBrands.Add(brand);
                        if (!string.IsNullOrEmpty(errorDetail))
                            brandFailureMessage[brand] = errorDetail;
                    }
                    else
                    {
                        drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedBrands.Add(brand);
                    brandFailureMessage[brand] = Flatten(ex);
                }
            }

            foreach (var def in request.Printers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var displayName = def.DisplayName.Trim();
                if (string.IsNullOrEmpty(displayName))
                {
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Error,
                        "Empty display name in printer definition",
                        displayName));
                    continue;
                }

                if (failedBrands.Contains(def.Brand))
                {
                    var text = brandFailureMessage.TryGetValue(def.Brand, out var m) && !string.IsNullOrEmpty(m)
                        ? m
                        : $"Driver not available for brand {def.Brand}.";
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.AbortedDriverMissing,
                        text,
                        displayName));
                    continue;
                }

                try
                {
                    if (await _remote.PrinterQueueExistsAsync(computer, request.DomainCredential, displayName, cancellationToken).ConfigureAwait(false))
                    {
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.SkippedAlreadyExists,
                            "Skipped — queue already exists",
                            displayName));
                        continue;
                    }

                    var driverOrder = PrinterCatalog.GetDriverResolutionOrder(def.Brand);
                    var resolvedDriver = DriverNameMatcher.ResolveInstalledDriverName(drivers, driverOrder);
                    if (resolvedDriver is null)
                    {
                        var describe = PrinterCatalog.DescribeAcceptableDrivers(def.Brand);
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.AbortedDriverMissing,
                            $"Driver not installed: {describe}",
                            displayName));
                        continue;
                    }

                    var portName = PrinterPortNaming.BuildPortName(def.PrinterHostAddress, def.PortNumber);
                    var protocol = MapProtocol(def.Protocol);
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Configuring,
                        "Creating port...",
                        displayName));
                    await _remote.CreateTcpPrinterPortAsync(
                        computer,
                        request.DomainCredential,
                        portName,
                        def.PrinterHostAddress,
                        def.PortNumber,
                        protocol,
                        cancellationToken).ConfigureAwait(false);

                    rollbackJournal.RecordPortCreated(computer, portName);

                    cancellationToken.ThrowIfCancellationRequested();

                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Configuring,
                        "Adding printer...",
                        displayName));
                    await _remote.AddPrinterAsync(
                        computer,
                        request.DomainCredential,
                        displayName,
                        resolvedDriver,
                        portName,
                        cancellationToken).ConfigureAwait(false);

                    rollbackJournal.RecordQueueCreated(computer, displayName, portName);

                    if (request.PrintTestPage)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.Configuring,
                            "Sending test page...",
                            displayName));
                        try
                        {
                            await _remote.PrintTestPageAsync(
                                computer,
                                request.DomainCredential,
                                displayName,
                                cancellationToken).ConfigureAwait(false);
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.CompletedSuccess,
                                "Done",
                                displayName));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.CompletedSuccess,
                                $"Done — test page failed: {Flatten(ex)}",
                                displayName));
                        }
                    }
                    else
                    {
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.CompletedSuccess,
                            "Done",
                            displayName));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Error,
                        Flatten(ex),
                        displayName));
                }
            }
        }
    }

    private static IReadOnlyList<PrinterBrand> DistinctBrandsInOrder(IReadOnlyList<PrinterQueueDefinition> printers)
    {
        var seen = new HashSet<PrinterBrand>();
        var list = new List<PrinterBrand>();
        foreach (var p in printers)
        {
            if (seen.Add(p.Brand))
                list.Add(p.Brand);
        }

        return list;
    }

    private async Task<(bool Success, string? ErrorForQueues)> TryInstallMissingDriverAsync(
        string computer,
        PrinterDeploymentRequest request,
        PrinterBrand brand,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        var acceptable = PrinterCatalog.GetDriverResolutionOrder(brand);
        var describe = PrinterCatalog.DescribeAcceptableDrivers(brand);

        var package = _localDrivers.TryGet(brand);
        if (package is null)
        {
            return (false, $"Driver not installed: {describe}. No local package available.");
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.InstallingDriver,
            $"Installing driver package '{package.InfFileName}' on {computer}...",
            null));

        var log = new Progress<string>(msg =>
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.InstallingDriver, msg, null)));

        try
        {
            await _remote.InstallPrinterDriverAsync(computer, request.DomainCredential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            return (false, $"Driver not installed: {describe}. install unsupported on this channel.");
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.DriverInstalledReconfirming,
            "Revalidating driver after install...",
            null));

        var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
        if (!DriverNameMatcher.IsAnyAcceptedDriverInstalled(drivers, acceptable))
        {
            var sample = string.Join(" | ", drivers.Take(10));
            return (false, $"Driver installed does not match expected. Expected one of: {describe}. Found: [{sample}]");
        }

        return (true, null);
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
