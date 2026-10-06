using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PrinterInstall.App.Localization;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Logging;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int DefaultDeployPort = 9100;
    private const TcpPrinterProtocol DefaultDeployProtocol = TcpPrinterProtocol.Raw;

    private readonly ISessionContext _session;
    private readonly PrinterDeploymentOrchestrator _orchestrator;
    private readonly DeploymentRollbackRunner _rollbackRunner;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalMachineIdentity _localMachineIdentity;
    private readonly IDeploymentNotificationService _notificationService;
    private readonly IConfirmationDialogService _dialogService;
    private readonly IThemeService? _themeService;
    private readonly IDiagnosticFileLogger? _diagnosticLogger;
    private CancellationTokenSource? _deployCts;
    private readonly ILocalDiagnosticCollector? _diagnosticCollector;
    private string _diagnosticContext = "";

    public MainViewModel(
        ISessionContext session,
        PrinterDeploymentOrchestrator orchestrator,
        DeploymentRollbackRunner rollbackRunner,
        IServiceProvider serviceProvider,
        LocalMachineIdentity localMachineIdentity,
        ILogExportService? logExportService = null,
        IDeploymentNotificationService? notificationService = null,
        IConfirmationDialogService? dialogService = null,
        IThemeService? themeService = null,
        IDiagnosticFileLogger? diagnosticLogger = null,
        ILocalDiagnosticCollector? diagnosticCollector = null)
    {
        _session = session;
        _orchestrator = orchestrator;
        _rollbackRunner = rollbackRunner;
        _serviceProvider = serviceProvider;
        _localMachineIdentity = localMachineIdentity;
        Log = new OperationLog(logExportService ?? new LogExportService(), diagnosticLogger, "MainDeploy");
        Log.PropertyChanged += OnLogChanged;
        _notificationService = notificationService ?? new DeploymentNotificationService();
        _dialogService = dialogService ?? new ConfirmationDialogService();
        _themeService = themeService;
        _diagnosticLogger = diagnosticLogger;
        _diagnosticCollector = diagnosticCollector;

        if (_themeService != null)
        {
            _isDarkMode = _themeService.IsDarkMode;
            _themeService.ThemeChanged += (_, _) =>
            {
                IsDarkMode = _themeService.IsDarkMode;
            };
        }

        PrinterRows.Add(new PrinterFormRowViewModel());
        PrinterRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanRemovePrinterRow));
            RemovePrinterRowCommand.NotifyCanExecuteChanged();
        };
        Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatusEmptyHint));
    }

    public bool ShowStatusEmptyHint => Targets.Count == 0;

    public bool CanRemovePrinterRow => PrinterRows.Count > 1;

    [ObservableProperty]
    private bool _isDarkMode;

    [RelayCommand]
    private void ToggleTheme()
    {
        if (_themeService != null)
        {
            _themeService.ToggleTheme();
            IsDarkMode = _themeService.IsDarkMode;
        }
        else
        {
            IsDarkMode = !IsDarkMode;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComputerCount))]
    [NotifyPropertyChangedFor(nameof(InvalidComputerCount))]
    [NotifyPropertyChangedFor(nameof(HasInvalidComputers))]
    private string _computersText = "";

    public bool HasInvalidComputers => InvalidComputerCount > 0;

    public int ComputerCount => ComputerNameListParser.Parse(ComputersText).Count;

    public int InvalidComputerCount =>
        ComputerNameListParser.Parse(ComputersText).Count(n => !ComputerNameValidator.IsPlausibleComputerName(n));

    [ObservableProperty]
    private bool _printTestPage;

    public OperationLog Log { get; }

    public string LogText
    {
        get => Log.Text;
        set => Log.Text = value;
    }

    private void OnLogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationLog.Text))
            return;

        OnPropertyChanged(nameof(LogText));
        OnPropertyChanged(nameof(CanExportLog));
        ExportLogCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private string _lastSummaryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportLog))]
    private bool _isDeployRunning;

    partial void OnIsDeployRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExportLog));
        DeployCommand.NotifyCanExecuteChanged();
        CancelDeployCommand.NotifyCanExecuteChanged();
        ExportLogCommand.NotifyCanExecuteChanged();
    }

    public bool CanExportLog => Log.CanExport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelDeployButtonText))]
    private bool _isCancellationRequested;

    public string CancelDeployButtonText => IsCancellationRequested ? "Cancelando…" : "Cancelar";

    partial void OnIsCancellationRequestedChanged(bool value) => CancelDeployCommand.NotifyCanExecuteChanged();

    public ObservableCollection<PrinterFormRowViewModel> PrinterRows { get; } = new();

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    [RelayCommand]
    private void AddThisComputer() =>
        ComputersText = ComputerListText.WithThisComputer(ComputersText, _localMachineIdentity);

    [RelayCommand]
    private void AddPrinterRow()
    {
        PrinterRows.Add(new PrinterFormRowViewModel());
    }

    [RelayCommand(CanExecute = nameof(CanRemovePrinterRow))]
    private void RemovePrinterRow(PrinterFormRowViewModel? row)
    {
        if (PrinterRows.Count <= 1)
            return;
        if (row is not null && PrinterRows.Contains(row))
        {
            PrinterRows.Remove(row);
            return;
        }

        PrinterRows.RemoveAt(PrinterRows.Count - 1);
    }

    [RelayCommand]
    private void CopySummaryToClipboard()
    {
        if (string.IsNullOrEmpty(LastSummaryText))
            return;
        try
        {
            Clipboard.SetText(LastSummaryText);
        }
        catch
        {
            // Clipboard may fail in rare cases; ignore
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportLog))]
    private void ExportLog()
    {
        Log.SetSensitiveValue(_session.Credential?.Password);
        var operatorId = _session.Credential is not null
            ? (string.IsNullOrEmpty(_session.Credential.Domain)
                ? _session.Credential.UserName
                : $@"{_session.Credential.Domain}\{_session.Credential.UserName}")
            : null;

        var targetSummaries = Targets.Select(t => (
            t.ComputerName,
            t.PrinterQueueName,
            TargetMachineStateDisplay.GetDisplay(t.State),
            (string?)t.Message
        ));

        Log.Export(
            $"PrinterInstall_Deploy_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt",
            logText => LogReportFormatter.FormatDeployReport(
                operatorId,
                _localMachineIdentity.GetPrimaryLocalName(),
                targetSummaries,
                logText,
                diagnosticLogPath: _diagnosticLogger?.CurrentLogFilePath,
                diagnosticContext: _diagnosticContext,
                diagnosticLogText: _diagnosticLogger?.ReadSessionLog()));
    }

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        LogText = "";
        Targets.Clear();
        LastSummaryText = "";
        _diagnosticContext = "";

        var cred = _session.Credential;
        if (cred is null)
        {
            Log.Append(UiStrings.Main_NotAuthenticated);
            return;
        }
        Log.SetSensitiveValue(cred.Password);

        var rawNames = ComputerNameListParser.Parse(ComputersText);
        if (rawNames.Count == 0)
        {
            Log.Append(UiStrings.Main_Validation_ComputersRequired);
            await _dialogService.ShowNoComputersWarningAsync();
            return;
        }

        foreach (var row in PrinterRows)
        {
            if (string.IsNullOrWhiteSpace(row.DisplayName))
            {
                Log.Append(UiStrings.Main_Validation_DisplayNameRequired);
                return;
            }

            if (string.IsNullOrWhiteSpace(row.PrinterHostAddress))
            {
                Log.Append(UiStrings.Main_Validation_PrinterHostRequired);
                return;
            }
        }

        var invertedRows = new List<(PrinterFormRowViewModel Row, string DisplayName, string HostAddress)>();
        foreach (var row in PrinterRows)
        {
            var trimmedDisplayName = row.DisplayName.Trim();
            var trimmedHost = row.PrinterHostAddress.Trim();

            if (PrinterHostValidator.DetectProbableInversion(trimmedDisplayName, trimmedHost))
            {
                invertedRows.Add((row, trimmedDisplayName, trimmedHost));
            }
        }

        if (invertedRows.Count > 0)
        {
            var inversionItems = invertedRows
                .Select(item => string.Format(UiStrings.Main_InversionDialogItemFormat, item.DisplayName, item.HostAddress))
                .ToList();

            var proceed = await _dialogService.ConfirmInversionCorrectionAsync(inversionItems);
            if (!proceed)
            {
                Log.Append(UiStrings.Main_DeployCancelledByInversionWarning);
                return;
            }

            // Inverter os campos automaticamente na interface e registrar log
            foreach (var item in invertedRows)
            {
                item.Row.DisplayName = item.HostAddress;
                item.Row.PrinterHostAddress = item.DisplayName;
                Log.Append(string.Format(UiStrings.Main_InversionCorrectedLogFormat, item.HostAddress, item.DisplayName));
            }
        }

        var definitions = new List<PrinterQueueDefinition>();
        foreach (var row in PrinterRows)
        {
            var trimmedDisplayName = row.DisplayName.Trim();
            var trimmedHost = row.PrinterHostAddress.Trim();

            if (!PrinterHostValidator.IsValidHostAddress(trimmedHost))
            {
                Log.Append(string.Format(UiStrings.Main_Validation_InvalidHostAddressFormat, trimmedHost));
                return;
            }

            if (row.Brand == PrinterBrand.Gainscha && row.GainschaLabelPreset is null)
            {
                Log.Append(UiStrings.Main_Validation_GainschaLabelPresetRequired);
                return;
            }

            definitions.Add(new PrinterQueueDefinition
            {
                Brand = row.Brand,
                DisplayName = trimmedDisplayName,
                PrinterHostAddress = trimmedHost,
                PortNumber = DefaultDeployPort,
                Protocol = DefaultDeployProtocol,
                GainschaLabelPreset = row.Brand == PrinterBrand.Gainscha ? row.GainschaLabelPreset : null
            });
        }

        var heuristicWarnings = PrinterBrandHeuristicsValidator.Inspect(definitions);
        if (heuristicWarnings.Count > 0)
        {
            var proceed = await _dialogService.ConfirmDeployWarningAsync(heuristicWarnings);
            if (!proceed)
            {
                Log.Append(UiStrings.Main_DeployCancelledByMismatchWarning);
                return;
            }
        }

        var validNames = new List<string>();
        foreach (var n in rawNames)
        {
            if (!ComputerNameValidator.IsPlausibleComputerName(n))
            {
                foreach (var def in definitions)
                {
                    var row = new TargetRowViewModel
                    {
                        ComputerName = n,
                        PrinterQueueName = def.DisplayName,
                        ExpectedPortName = PrinterPortNaming.BuildPortName(def.PrinterHostAddress, DefaultDeployPort),
                        State = TargetMachineState.Error,
                        Message = UiStrings.Main_InvalidComputerNameFormat
                    };
                    Targets.Add(row);
                    ConfigureTargetRowDisplay(row, n);
                }

                continue;
            }

            validNames.Add(n);
        }

        if (validNames.Count == 0)
            return;

        foreach (var n in validNames)
        {
            foreach (var def in definitions)
            {
                var row = new TargetRowViewModel
                {
                    ComputerName = n,
                    PrinterQueueName = def.DisplayName,
                    ExpectedPortName = PrinterPortNaming.BuildPortName(def.PrinterHostAddress, DefaultDeployPort),
                    State = TargetMachineState.Pending
                };
                Targets.Add(row);
                ConfigureTargetRowDisplay(row, n);
            }
        }

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = validNames,
            Printers = definitions,
            DomainCredential = cred,
            PrintTestPage = PrintTestPage
        };

        var journal = new DeploymentRollbackJournal();
        _deployCts = new CancellationTokenSource();
        var deployToken = _deployCts.Token;
        IsCancellationRequested = false;
        IsDeployRunning = true;
        var started = DateTimeOffset.Now;
        var runId = Guid.NewGuid().ToString("N");
        var plan = new StringBuilder();
        plan.AppendLine($"Execução: {runId}; sessão do aplicativo: {_diagnosticLogger?.SessionId}; início: {started:O}");
        plan.AppendLine($"Conta informada: {PrinterInstall.Core.Auth.CredentialHelper.BuildCredentialUserName(cred)}; teste de impressão: {request.PrintTestPage}");
        foreach (var computer in validNames)
            plan.AppendLine($"Destino: {computer}; caminho: {(_localMachineIdentity.IsLocalMachine(computer) ? "LOCAL" : "REMOTO (WMI/SMB)")}");
        foreach (var printer in definitions)
            plan.AppendLine($"Fila planejada: {printer.DisplayName}; fabricante: {printer.Brand}; endereço: {printer.PrinterHostAddress}; porta: {printer.PortNumber}; protocolo: {printer.Protocol}; etiqueta: {printer.GainschaLabelPreset}");
        _diagnosticContext = plan.ToString();
        Log.Append($"Início do deploy. Execução: {runId}.");

        var identityBlockReasons = new List<string>();
        var progress = new SynchronousProgress<DeploymentProgressEvent>(e =>
        {
            OperationLog.RunOnUi(() =>
            {
                if (e.State is (TargetMachineState.PrinterIdentityMismatch or TargetMachineState.PrinterIdentityUnknown) &&
                    !string.IsNullOrWhiteSpace(e.Detail))
                    identityBlockReasons.Add(e.Detail);

                if (e.PrinterQueueName is null)
                {
                    foreach (var row in Targets.Where(t => t.ComputerName == e.ComputerName))
                    {
                        row.State = e.State;
                        row.Message = e.Message;
                    }
                }
                else
                {
                    var line = Targets.FirstOrDefault(
                        t => t.ComputerName == e.ComputerName && t.PrinterQueueName == e.PrinterQueueName);
                    if (line is not null)
                    {
                        line.State = e.State;
                        line.Message = e.Message;
                    }
                }

                var q = e.PrinterQueueName ?? "geral";
                var stateText = TargetMachineStateDisplay.GetDisplay(e.State);
                var logMessage = e.Message.StartsWith(stateText, StringComparison.OrdinalIgnoreCase)
                    ? e.Message : $"{stateText}: {e.Message}";
                Log.Append($"{e.ComputerName} [{q}]: {logMessage}");
            });
        });

        var diagnosticProgress = new SynchronousProgress<string>(msg =>
        {
            OperationLog.RunOnUi(() => Log.Append(msg));
        });

        try
        {
            var context = await Task.Run(() => _diagnosticCollector is null
                ? Task.FromResult(LocalDiagnosticCollector.CaptureBasicContext())
                : _diagnosticCollector.CollectAsync(deployToken), deployToken);
            _diagnosticContext = plan.ToString() + context;
            _diagnosticLogger?.LogDebug(_diagnosticContext, "DeployContext");
            // APIs nativas síncronas nunca devem bloquear o Dispatcher e impedir o clique em Cancelar.
            await Task.Run(() => _orchestrator.RunAsync(request, journal, progress, deployToken, diagnosticProgress), deployToken);
            deployToken.ThrowIfCancellationRequested();

            LastSummaryText = BuildSummaryText();
            NotifyDeployCompletion();
        }
        catch (OperationCanceledException)
        {
            Log.Append("Deploy interrompido. Verificando alterações desta execução.");
            OperationLog.RunOnUi(MarkIntermediateTargetsAsDeployCancelled);

            if (journal.HasRollbackWork)
            {
                Log.Append(UiStrings.Main_DeployRollbackStarting);
                var rbProgress = new SynchronousProgress<PrinterRemovalProgressEvent>(e =>
                {
                    OperationLog.RunOnUi(() =>
                    {
                        ApplyRollbackProgress(e, journal);
                        Log.Append($"{e.ComputerName}: {e.Message}");
                    });
                });
                try
                {
                    await Task.Run(() => _rollbackRunner.RunAsync(journal, cred, rbProgress, CancellationToken.None));
                    Log.Append(UiStrings.Main_DeployRollbackFinished);
                }
                catch (Exception ex)
                {
                    _diagnosticLogger?.LogError("Falha durante a reversão.", "MainDeploy", ex);
                    Log.Append(string.Format(UiStrings.Main_DeployRollbackErrorFormat, ex.Message));
                }
            }

            OperationLog.RunOnUi(() =>
            {
                foreach (var row in Targets)
                {
                    if (row.State is TargetMachineState.RollbackRemovingQueue or TargetMachineState.RollbackRemovingPort)
                    {
                        row.State = TargetMachineState.RolledBack;
                    }
                    else if (IsIntermediateDeployState(row.State))
                    {
                        row.State = TargetMachineState.DeployCancelled;
                        row.Message = UiStrings.Main_DeployCancelledRowMessage;
                    }
                }
            });

            Log.Append(UiStrings.Main_DeployCooperativeCancelHint);
            LastSummaryText = BuildSummaryText();
            _notificationService.NotifyWarning();
        }
        catch (Exception ex)
        {
            Log.Append("Falha inesperada no deploy: " + DiagnosticLogFormatter.FormatExceptionDetails(ex));
            _diagnosticLogger?.LogError("Falha inesperada no deploy.", "MainDeploy", ex);
            LastSummaryText = BuildSummaryText();
            _notificationService.NotifyWarning();
        }
        finally
        {
            _diagnosticContext += $"\nFim da execução: {DateTimeOffset.Now:O}; duração: {(DateTimeOffset.Now - started).TotalSeconds:F1}s; cancelamento solicitado: {IsCancellationRequested}.\n";
            Log.Append($"Fim do deploy. Execução: {runId}; duração: {(DateTimeOffset.Now - started).TotalSeconds:F1}s; cancelamento solicitado: {IsCancellationRequested}.");
            _deployCts?.Dispose();
            _deployCts = null;
            IsDeployRunning = false;
        }

        var distinctIdentityBlockReasons = identityBlockReasons
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctIdentityBlockReasons.Length > 0)
        {
            var confirmedMismatch = !Targets.Any(t => t.State == TargetMachineState.PrinterIdentityUnknown);
            await _dialogService.ShowPrinterIdentityBlockAsync(distinctIdentityBlockReasons, confirmedMismatch);
        }
        else if (!string.IsNullOrEmpty(LastSummaryText) && Application.Current is not null)
        {
            MessageBox.Show(LastSummaryText, UiStrings.Main_SummaryDialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool CanDeploy() => !IsDeployRunning;

    [RelayCommand(CanExecute = nameof(CanCancelDeploy))]
    private void CancelDeploy()
    {
        if (_deployCts is null || IsCancellationRequested) return;
        IsCancellationRequested = true;
        Log.Append(UiStrings.Main_DeployCancelRequested + " Encerrando a etapa atual; novas etapas não serão iniciadas.");
        foreach (var row in Targets.Where(r => IsIntermediateDeployState(r.State)))
            row.Message = "Cancelamento solicitado — aguardando encerramento";
        _deployCts.Cancel();
    }

    private bool CanCancelDeploy() => IsDeployRunning && !IsCancellationRequested;

    private void MarkIntermediateTargetsAsDeployCancelled()
    {
        foreach (var row in Targets.Where(r => IsIntermediateDeployState(r.State)))
        {
            row.State = TargetMachineState.DeployCancelled;
            row.Message = UiStrings.Main_DeployCancelledRowMessage;
        }
    }

    private static bool IsIntermediateDeployState(TargetMachineState s) =>
        s is TargetMachineState.Pending
            or TargetMachineState.IdentifyingPrinter
            or TargetMachineState.ValidatingPrinter
            or TargetMachineState.ContactingRemote
            or TargetMachineState.ValidatingDriver
            or TargetMachineState.InstallingDriver
            or TargetMachineState.DriverInstalledReconfirming
            or TargetMachineState.Configuring;

    private void ApplyRollbackProgress(PrinterRemovalProgressEvent e, DeploymentRollbackJournal journal)
    {
        switch (e.State)
        {
            case PrinterRemovalProgressState.ContactingRemote:
                foreach (var row in Targets.Where(t =>
                             string.Equals(t.ComputerName, e.ComputerName, StringComparison.OrdinalIgnoreCase)
                             && JournalTouchesRow(journal, t)))
                {
                    row.State = TargetMachineState.RollbackRemovingQueue;
                    row.Message = UiStrings.Main_RollbackPreparingOnHost;
                }
                break;
            case PrinterRemovalProgressState.RemovingQueue:
                if (FindTargetRow(e.ComputerName, e.PrinterQueueName) is { } rq)
                {
                    rq.State = TargetMachineState.RollbackRemovingQueue;
                    rq.Message = e.Message;
                }
                break;
            case PrinterRemovalProgressState.RemovingOrphanPort:
                if (ResolveRollbackRow(e) is { } rp)
                {
                    rp.State = TargetMachineState.RollbackRemovingPort;
                    rp.Message = e.Message;
                }
                break;
            case PrinterRemovalProgressState.RollbackSucceeded:
                if (ResolveRollbackRow(e) is { } ok)
                {
                    ok.State = TargetMachineState.RolledBack;
                    ok.Message = e.Message;
                }
                break;
            case PrinterRemovalProgressState.Warning:
                if (ResolveRollbackRow(e) is { } w)
                {
                    w.State = TargetMachineState.Error;
                    w.Message = e.Message;
                }
                break;
            case PrinterRemovalProgressState.Error:
                if (ResolveRollbackRow(e) is { } errRow)
                {
                    errRow.State = TargetMachineState.Error;
                    errRow.Message = e.Message;
                }
                break;
        }
    }

    private static bool JournalTouchesRow(DeploymentRollbackJournal journal, TargetRowViewModel row) =>
        journal.QueueEntries.Any(q => string.Equals(q.ComputerName, row.ComputerName, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(q.PrinterName, row.PrinterQueueName, StringComparison.OrdinalIgnoreCase))
        || journal.PortOnlyEntries.Any(p => string.Equals(p.Computer, row.ComputerName, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(p.PortName, row.ExpectedPortName, StringComparison.OrdinalIgnoreCase));

    private void ConfigureTargetRowDisplay(TargetRowViewModel row, string computerName)
    {
        var isLocal = _localMachineIdentity.IsLocalMachine(computerName);
        row.IsLocalMachine = isLocal;
        row.ComputerNameDisplay = isLocal
            ? $"{computerName} {UiStrings.Main_LocalComputerSuffix}"
            : computerName;
    }

    private TargetRowViewModel? FindTargetRow(string computer, string? printerQueue)
    {
        if (string.IsNullOrEmpty(printerQueue))
            return null;
        return Targets.FirstOrDefault(t =>
            string.Equals(t.ComputerName, computer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.PrinterQueueName, printerQueue, StringComparison.OrdinalIgnoreCase));
    }

    private TargetRowViewModel? FindTargetRowByPort(string computer, string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return null;
        return Targets.FirstOrDefault(t =>
            string.Equals(t.ComputerName, computer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ExpectedPortName, portName, StringComparison.OrdinalIgnoreCase));
    }

    private TargetRowViewModel? ResolveRollbackRow(PrinterRemovalProgressEvent e) =>
        FindTargetRow(e.ComputerName, e.PrinterQueueName) ?? FindTargetRowByPort(e.ComputerName, e.PortName);

    private string BuildSummaryText()
    {
        if (Targets.Count == 0)
            return string.Empty;

        var ok = 0;
        var skipped = 0;
        var err = 0;
        var aborted = 0;
        var rolledBack = 0;
        var deployCancelled = 0;
        var other = 0;
        foreach (var t in Targets)
        {
            switch (t.State)
            {
                case TargetMachineState.CompletedSuccess: ok++; break;
                case TargetMachineState.SkippedAlreadyExists: skipped++; break;
                case TargetMachineState.Error: err++; break;
                case TargetMachineState.PrinterIdentityMismatch: err++; break;
                case TargetMachineState.PrinterIdentityUnknown: err++; break;
                case TargetMachineState.AbortedDriverMissing: aborted++; break;
                case TargetMachineState.RolledBack: rolledBack++; break;
                case TargetMachineState.DeployCancelled: deployCancelled++; break;
                default: other++; break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(UiStrings.Main_SummaryLineFormat, ok, skipped, err, aborted));
        if (other > 0)
            sb.AppendLine(string.Format(UiStrings.Main_SummaryOtherFormat, other));
        if (rolledBack > 0)
            sb.AppendLine(string.Format(UiStrings.Main_SummaryRolledBackFormat, rolledBack));
        if (deployCancelled > 0)
            sb.AppendLine(string.Format(UiStrings.Main_SummaryDeployCancelledFormat, deployCancelled));

        if (err > 0 || aborted > 0)
        {
            foreach (var t in Targets.Where(x => x.State is TargetMachineState.Error or TargetMachineState.AbortedDriverMissing or TargetMachineState.PrinterIdentityMismatch or TargetMachineState.PrinterIdentityUnknown))
            {
                sb.AppendLine(
                    string.Format(UiStrings.Main_SummaryFailureLineFormat, t.ComputerName, t.PrinterQueueName, t.Message));
            }
        }

        return sb.ToString();
    }

    private void NotifyDeployCompletion()
    {
        if (Targets.Count == 0)
            return;

        var successCount = Targets.Count(t => t.State is TargetMachineState.CompletedSuccess or TargetMachineState.SkippedAlreadyExists);
        var errorCount = Targets.Count(t => t.State is TargetMachineState.Error or TargetMachineState.AbortedDriverMissing or TargetMachineState.PrinterIdentityMismatch or TargetMachineState.PrinterIdentityUnknown);
        var cancelCount = Targets.Count(t => t.State is TargetMachineState.DeployCancelled or TargetMachineState.RolledBack);

        if (cancelCount > 0)
        {
            _notificationService.NotifyWarning();
        }
        else if (errorCount == 0 && successCount > 0)
        {
            _notificationService.NotifySuccess();
        }
        else if (successCount == 0 && errorCount > 0)
        {
            _notificationService.NotifyError();
        }
        else
        {
            _notificationService.NotifyWarning();
        }
    }

    [RelayCommand]
    private void OpenLogFolder() => Log.OpenFolder();

    [RelayCommand]
    private void OpenRemovalWizard()
    {
        var window = _serviceProvider.GetRequiredService<Views.RemovalWizardWindow>();
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsLoaded && w.IsVisible && !ReferenceEquals(w, window));
        if (owner is not null)
            window.Owner = owner;
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenPrinterNetworkTest()
    {
        var window = _serviceProvider.GetRequiredService<Views.PrinterNetworkTestWindow>();
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsLoaded && w.IsVisible && !ReferenceEquals(w, window));
        if (owner is not null)
            window.Owner = owner;
        window.ShowDialog();
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }
}
