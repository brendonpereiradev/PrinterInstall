using System.Collections.Concurrent;
using System.Net;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterDeploymentOrchestrator
{
    public const int DefaultMaxDegreeOfParallelism = 4;
    private static readonly TimeSpan SpoolerSettleDelay = TimeSpan.FromSeconds(2);

    private readonly IRemotePrinterOperations _remote;
    private readonly ILocalDriverPackageCatalog _localDrivers;
    private readonly IDirectRawPrinterTestService _rawTestService;
    private readonly IFastHostReachabilityChecker _reachabilityChecker;
    private readonly IPrinterPingService _printerPingService;
    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly int? _configuredMaxDegreeOfParallelism;

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote)
        : this(remote, new NullLocalDriverPackageCatalog(), new DirectRawPrinterTestService(), new NullFastHostReachabilityChecker(), new NullPrinterPingService(), TransientRetryHelper.DefaultMaxAttempts, TransientRetryHelper.DefaultInitialDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers)
        : this(remote, localDrivers, new DirectRawPrinterTestService(), new NullFastHostReachabilityChecker(), new NullPrinterPingService(), TransientRetryHelper.DefaultMaxAttempts, TransientRetryHelper.DefaultInitialDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService)
        : this(remote, localDrivers, rawTestService, new NullFastHostReachabilityChecker(), new NullPrinterPingService(), TransientRetryHelper.DefaultMaxAttempts, TransientRetryHelper.DefaultInitialDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        int maxRetryAttempts,
        TimeSpan retryDelay)
        : this(remote, localDrivers, new DirectRawPrinterTestService(), new NullFastHostReachabilityChecker(), new NullPrinterPingService(), maxRetryAttempts, retryDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService,
        int maxRetryAttempts,
        TimeSpan retryDelay)
        : this(remote, localDrivers, rawTestService, new NullFastHostReachabilityChecker(), new NullPrinterPingService(), maxRetryAttempts, retryDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService,
        IFastHostReachabilityChecker reachabilityChecker,
        int maxRetryAttempts,
        TimeSpan retryDelay,
        int? configuredMaxDegreeOfParallelism = null)
        : this(remote, localDrivers, rawTestService, reachabilityChecker, new NullPrinterPingService(), maxRetryAttempts, retryDelay, configuredMaxDegreeOfParallelism)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService,
        IFastHostReachabilityChecker reachabilityChecker,
        IPrinterPingService printerPingService,
        int maxRetryAttempts,
        TimeSpan retryDelay)
        : this(remote, localDrivers, rawTestService, reachabilityChecker, printerPingService, maxRetryAttempts, retryDelay, null)
    {
    }

    public PrinterDeploymentOrchestrator(
        IRemotePrinterOperations remote,
        ILocalDriverPackageCatalog localDrivers,
        IDirectRawPrinterTestService rawTestService,
        IFastHostReachabilityChecker reachabilityChecker,
        IPrinterPingService? printerPingService,
        int maxRetryAttempts,
        TimeSpan retryDelay,
        int? configuredMaxDegreeOfParallelism = null)
    {
        _remote = remote;
        _localDrivers = localDrivers;
        _rawTestService = rawTestService;
        _reachabilityChecker = reachabilityChecker ?? new NullFastHostReachabilityChecker();
        _printerPingService = printerPingService ?? new NullPrinterPingService();
        _maxRetryAttempts = maxRetryAttempts;
        _retryDelay = retryDelay;
        _configuredMaxDegreeOfParallelism = configuredMaxDegreeOfParallelism;
    }

    public async Task RunAsync(
        PrinterDeploymentRequest request,
        DeploymentRollbackJournal rollbackJournal,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken = default,
        IProgress<string>? diagnosticLog = null)
    {
        var maxDegree = _configuredMaxDegreeOfParallelism
            ?? (request.MaxDegreeOfParallelism > 0 ? request.MaxDegreeOfParallelism : DefaultMaxDegreeOfParallelism);

        var printerPingCache = new ConcurrentDictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);

        if (maxDegree <= 1 || request.TargetComputerNames.Count <= 1)
        {
            foreach (var computer in request.TargetComputerNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessSingleTargetAsync(computer, request, rollbackJournal, progress, printerPingCache, cancellationToken, diagnosticLog).ConfigureAwait(false);
            }
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegree,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(request.TargetComputerNames, parallelOptions, async (computer, ct) =>
            {
                await ProcessSingleTargetAsync(computer, request, rollbackJournal, progress, printerPingCache, ct, diagnosticLog).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && (ex is TaskCanceledException || ex.GetType() != typeof(OperationCanceledException)))
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private async Task ProcessSingleTargetAsync(
        string computer,
        PrinterDeploymentRequest request,
        DeploymentRollbackJournal rollbackJournal,
        IProgress<DeploymentProgressEvent> progress,
        ConcurrentDictionary<string, Task<bool>> printerPingCache,
        CancellationToken cancellationToken,
        IProgress<string>? diagnosticLog = null)
    {
        progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ContactingRemote, "Conectando", null));

        var (isReachable, reachabilityError) = await _reachabilityChecker.CheckReachabilityAsync(computer, cancellationToken).ConfigureAwait(false);
        if (!isReachable)
        {
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, "Host inacessível", null));
            return;
        }

        IReadOnlyList<string> drivers;
        try
        {
            drivers = await TransientRetryHelper.ExecuteWithRetryAsync(
                ct => _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, ct),
                maxAttempts: _maxRetryAttempts,
                initialDelay: _retryDelay,
                onRetry: (ex, attempt, delay) =>
                {
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.ContactingRemote,
                        $"Falha transitória ({Flatten(ex)}). Tentando novamente em {delay.TotalSeconds:F0}s (tentativa {attempt + 1}/{_maxRetryAttempts})...",
                        null));
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, Flatten(ex), null));
            return;
        }

            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ValidatingDriver, "Verificando drivers", null));

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
                    var (ok, errorDetail) = await TryInstallMissingDriverAsync(computer, request, brand, progress, cancellationToken, diagnosticLog)
                        .ConfigureAwait(false);
                    if (!ok)
                    {
                        failedBrands.Add(brand);
                        if (!string.IsNullOrEmpty(errorDetail))
                            brandFailureMessage[brand] = errorDetail;
                    }
                    else
                    {
                        drivers = await TransientRetryHelper.ExecuteWithRetryAsync(
                            ct => _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, ct),
                            maxAttempts: _maxRetryAttempts,
                            initialDelay: _retryDelay,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
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
                        "Nome inválido",
                        displayName));
                    continue;
                }

                if (failedBrands.Contains(def.Brand))
                {
                    var text = brandFailureMessage.TryGetValue(def.Brand, out var m) && !string.IsNullOrEmpty(m)
                        ? m
                        : "Driver ausente";
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.AbortedDriverMissing,
                        text,
                        displayName));
                    continue;
                }

                try
                {
                    var queueExists = await TransientRetryHelper.ExecuteWithRetryAsync(
                        ct => _remote.PrinterQueueExistsAsync(computer, request.DomainCredential, displayName, ct),
                        maxAttempts: _maxRetryAttempts,
                        initialDelay: _retryDelay,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (queueExists)
                    {
                        if (def.Brand == PrinterBrand.Gainscha)
                        {
                            if (def.GainschaLabelPreset is null)
                            {
                                progress.Report(new DeploymentProgressEvent(
                                    computer,
                                    TargetMachineState.Error,
                                    "Etiqueta obrigatória",
                                    displayName));
                                continue;
                            }

                            var (applied, errorDetail) = await TryApplyGainschaLabelPresetAsync(
                                computer,
                                request,
                                displayName,
                                def.GainschaLabelPreset.Value,
                                revertOnFailure: false,
                                portName: null,
                                rollbackJournal,
                                progress,
                                cancellationToken,
                                "Configurando etiqueta").ConfigureAwait(false);

                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                applied ? TargetMachineState.CompletedSuccess : TargetMachineState.Error,
                                applied ? "Etiqueta configurada" : "Falha etiqueta",
                                displayName));
                            continue;
                        }

                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.SkippedAlreadyExists,
                            "Já existe",
                            displayName));
                        continue;
                    }

                    var driverOrder = PrinterCatalog.GetDriverResolutionOrder(def.Brand);
                    var resolvedDriver = DriverNameMatcher.ResolveInstalledDriverName(drivers, driverOrder);
                    if (resolvedDriver is null)
                    {
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.AbortedDriverMissing,
                            "Driver ausente",
                            displayName));
                        continue;
                    }

                    var host = def.PrinterHostAddress?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(host))
                    {
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.ContactingRemote,
                            "Testando ping",
                            displayName));

                        var isPrinterReachable = await printerPingCache.GetOrAdd(
                            host,
                            h => _printerPingService.PingAsync(h, TimeSpan.FromSeconds(2), cancellationToken)
                        ).ConfigureAwait(false);

                        if (!isPrinterReachable)
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.Error,
                                "Impressora offline",
                                displayName));
                            continue;
                        }
                    }

                    var portName = PrinterPortNaming.BuildPortName(host, def.PortNumber);
                    var protocol = MapProtocol(def.Protocol);
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Configuring,
                        "Criando porta",
                        displayName));
                    await TransientRetryHelper.ExecuteWithRetryAsync(
                        ct => _remote.CreateTcpPrinterPortAsync(
                            computer,
                            request.DomainCredential,
                            portName,
                            host,
                            def.PortNumber,
                            protocol,
                            ct),
                        maxAttempts: _maxRetryAttempts,
                        initialDelay: _retryDelay,
                        onRetry: (ex, attempt, delay) =>
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.Configuring,
                                $"Falha transitória ao criar porta ({Flatten(ex)}). Tentando novamente em {delay.TotalSeconds:F0}s (tentativa {attempt + 1}/{_maxRetryAttempts})...",
                                displayName));
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    rollbackJournal.RecordPortCreated(computer, portName);

                    cancellationToken.ThrowIfCancellationRequested();

                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.Configuring,
                        "Instalando impressora",
                        displayName));
                    await TransientRetryHelper.ExecuteWithRetryAsync(
                        ct => _remote.AddPrinterAsync(
                            computer,
                            request.DomainCredential,
                            displayName,
                            resolvedDriver,
                            portName,
                            ct),
                        maxAttempts: _maxRetryAttempts,
                        initialDelay: _retryDelay,
                        onRetry: (ex, attempt, delay) =>
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.Configuring,
                                $"Falha transitória ao adicionar impressora ({Flatten(ex)}). Tentando novamente em {delay.TotalSeconds:F0}s (tentativa {attempt + 1}/{_maxRetryAttempts})...",
                                displayName));
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    rollbackJournal.RecordQueueCreated(computer, displayName, portName);

                    if (def.Brand == PrinterBrand.Gainscha)
                    {
                        if (def.GainschaLabelPreset is null)
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.Error,
                                "Etiqueta obrigatória",
                                displayName));
                            await RevertUnjournaledQueueAsync(
                                computer,
                                request.DomainCredential,
                                displayName,
                                portName,
                                rollbackJournal,
                                cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        var (presetApplied, _) = await TryApplyGainschaLabelPresetAsync(
                            computer,
                            request,
                            displayName,
                            def.GainschaLabelPreset.Value,
                            revertOnFailure: true,
                            portName,
                            rollbackJournal,
                            progress,
                            cancellationToken,
                            "Configurando etiqueta").ConfigureAwait(false);
                        if (!presetApplied)
                        {
                            continue;
                        }
                    }

                    if (request.PrintTestPage)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.Configuring,
                            "Enviando teste",
                            displayName));
                        try
                        {
                            var rawSuccess = false;
                            if (def.Brand == PrinterBrand.Gainscha && !string.IsNullOrWhiteSpace(def.PrinterHostAddress))
                            {
                                var rawResult = await _rawTestService.RunAsync(
                                    def.PrinterHostAddress,
                                    def.Brand,
                                    def.GainschaLabelPreset,
                                    cancellationToken).ConfigureAwait(false);
                                if (rawResult.Success)
                                    rawSuccess = true;
                            }

                            if (!rawSuccess)
                            {
                                await _remote.PrintTestPageAsync(
                                    computer,
                                    request.DomainCredential,
                                    displayName,
                                    cancellationToken).ConfigureAwait(false);
                            }

                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.CompletedSuccess,
                                "Concluído",
                                displayName));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            progress.Report(new DeploymentProgressEvent(
                                computer,
                                TargetMachineState.CompletedSuccess,
                                "Falha teste",
                                displayName));
                        }
                    }
                    else
                    {
                        progress.Report(new DeploymentProgressEvent(
                            computer,
                            TargetMachineState.CompletedSuccess,
                            "Concluído",
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
        CancellationToken cancellationToken,
        IProgress<string>? diagnosticLog = null)
    {
        var acceptable = PrinterCatalog.GetDriverResolutionOrder(brand);
        var describe = PrinterCatalog.DescribeAcceptableDrivers(brand);

        var package = _localDrivers.TryGet(brand);
        if (package is null)
        {
            return (false, $"Driver não instalado: {describe}. Pacote local não encontrado.");
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.InstallingDriver,
            "Instalando driver",
            null));

        var log = new Progress<string>(msg =>
        {
            diagnosticLog?.Report($"{computer}: {msg}");
        });

        try
        {
            await _remote.InstallPrinterDriverAsync(computer, request.DomainCredential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            return (false, $"Driver não instalado: {describe}. Instalação não suportada neste canal.");
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.DriverInstalledReconfirming,
            "Confirmando driver",
            null));

        var drivers = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct => _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, ct),
            maxAttempts: _maxRetryAttempts,
            initialDelay: _retryDelay,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!DriverNameMatcher.IsAnyAcceptedDriverInstalled(drivers, acceptable))
        {
            var sample = string.Join(" | ", drivers.Take(10));
            return (false, $"Driver instalado incompatível. Esperado: {describe}. Encontrado: [{sample}]");
        }

        return (true, null);
    }

    private async Task<(bool Success, string? ErrorDetail)> TryApplyGainschaLabelPresetAsync(
        string computer,
        PrinterDeploymentRequest request,
        string displayName,
        GainschaLabelPreset preset,
        bool revertOnFailure,
        string? portName,
        DeploymentRollbackJournal rollbackJournal,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken,
        string configuringMessage)
    {
        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.Configuring,
            configuringMessage,
            displayName));

        await Task.Delay(SpoolerSettleDelay, cancellationToken).ConfigureAwait(false);

        try
        {
            await _remote.ConfigureGainschaLabelPresetAsync(
                computer,
                request.DomainCredential,
                displayName,
                preset,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = Flatten(ex);
            if (revertOnFailure && portName is not null)
            {
                progress.Report(new DeploymentProgressEvent(
                    computer,
                    TargetMachineState.Configuring,
                    "Falha na preferência de etiqueta — revertendo fila e porta...",
                    displayName));

                await RevertUnjournaledQueueAsync(
                    computer,
                    request.DomainCredential,
                    displayName,
                    portName,
                    rollbackJournal,
                    cancellationToken).ConfigureAwait(false);

                progress.Report(new DeploymentProgressEvent(
                    computer,
                    TargetMachineState.Error,
                    $"Revertido — preferência de etiqueta não aplicada: {detail}",
                    displayName));
            }

            return (false, detail);
        }

        if (revertOnFailure)
        {
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.Configuring,
                "Preferência de etiqueta aplicada.",
                displayName));
        }

        return (true, null);
    }

    private async Task RevertUnjournaledQueueAsync(
        string computer,
        NetworkCredential credential,
        string displayName,
        string portName,
        DeploymentRollbackJournal rollbackJournal,
        CancellationToken cancellationToken)
    {
        try
        {
            await _remote.RemovePrinterQueueAsync(computer, credential, displayName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort — continue to port cleanup.
        }

        try
        {
            var count = await _remote.CountPrintersUsingPortAsync(computer, credential, portName, cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                await _remote.RemoveTcpPrinterPortAsync(computer, credential, portName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }

        rollbackJournal.AbandonQueue(computer, displayName, portName);
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
