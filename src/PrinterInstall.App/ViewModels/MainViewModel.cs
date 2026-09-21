using System.Collections.ObjectModel;
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
    private readonly ILogExportService _logExportService;
    private readonly IDeploymentNotificationService _notificationService;
    private readonly IConfirmationDialogService _dialogService;
    private readonly IThemeService? _themeService;
    private CancellationTokenSource? _deployCts;

    public MainViewModel(
        ISessionContext session,
        PrinterDeploymentOrchestrator orchestrator,
        DeploymentRollbackRunner rollbackRunner,
        IServiceProvider serviceProvider,
        LocalMachineIdentity localMachineIdentity,
        ILogExportService? logExportService = null,
        IDeploymentNotificationService? notificationService = null,
        IConfirmationDialogService? dialogService = null,
        IThemeService? themeService = null)
    {
        _session = session;
        _orchestrator = orchestrator;
        _rollbackRunner = rollbackRunner;
        _serviceProvider = serviceProvider;
        _localMachineIdentity = localMachineIdentity;
        _logExportService = logExportService ?? new LogExportService();
        _notificationService = notificationService ?? new DeploymentNotificationService();
        _dialogService = dialogService ?? new ConfirmationDialogService();
        _themeService = themeService;

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
    private string _computersText = "";

    [ObservableProperty]
    private bool _printTestPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportLog))]
    private string _logText = "";

    partial void OnLogTextChanged(string value)
    {
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

    public bool CanExportLog => !IsDeployRunning && !string.IsNullOrWhiteSpace(LogText);

    public ObservableCollection<PrinterFormRowViewModel> PrinterRows { get; } = new();

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    [RelayCommand]
    private void AddThisComputer()
    {
        var existing = ComputerNameListParser.Parse(ComputersText);
        if (existing.Any(_localMachineIdentity.IsLocalMachine))
            return;

        var name = _localMachineIdentity.GetPrimaryLocalName();
        ComputersText = string.IsNullOrWhiteSpace(ComputersText)
            ? name
            : ComputersText.TrimEnd() + Environment.NewLine + name;
    }

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
        if (string.IsNullOrWhiteSpace(LogText))
            return;

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

        var report = LogReportFormatter.FormatDeployReport(
            operatorId,
            _localMachineIdentity.GetPrimaryLocalName(),
            targetSummaries,
            LogText);

        var defaultFileName = $"PrinterInstall_Deploy_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        var result = _logExportService.ExportLog(defaultFileName, report);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.FilePath))
        {
            AppendLog(string.Format(UiStrings.Main_LogExportSuccessFormat, result.FilePath));
        }
        else if (!result.IsCancelled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AppendLog(string.Format(UiStrings.Main_LogExportErrorFormat, result.ErrorMessage));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        LogText = "";
        Targets.Clear();
        LastSummaryText = "";

        var cred = _session.Credential;
        if (cred is null)
        {
            AppendLog(UiStrings.Main_NotAuthenticated);
            return;
        }

        var rawNames = ComputerNameListParser.Parse(ComputersText);
        if (rawNames.Count == 0)
        {
            AppendLog(UiStrings.Main_Validation_ComputersRequired);
            await _dialogService.ShowNoComputersWarningAsync();
            return;
        }

        foreach (var row in PrinterRows)
        {
            if (string.IsNullOrWhiteSpace(row.DisplayName))
            {
                AppendLog(UiStrings.Main_Validation_DisplayNameRequired);
                return;
            }

            if (string.IsNullOrWhiteSpace(row.PrinterHostAddress))
            {
                AppendLog(UiStrings.Main_Validation_PrinterHostRequired);
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
                AppendLog(UiStrings.Main_DeployCancelledByInversionWarning);
                return;
            }

            // Inverter os campos automaticamente na interface e registrar log
            foreach (var item in invertedRows)
            {
                item.Row.DisplayName = item.HostAddress;
                item.Row.PrinterHostAddress = item.DisplayName;
                AppendLog(string.Format(UiStrings.Main_InversionCorrectedLogFormat, item.HostAddress, item.DisplayName));
            }
        }

        var definitions = new List<PrinterQueueDefinition>();
        foreach (var row in PrinterRows)
        {
            var trimmedDisplayName = row.DisplayName.Trim();
            var trimmedHost = row.PrinterHostAddress.Trim();

            if (!PrinterHostValidator.IsValidHostAddress(trimmedHost))
            {
                AppendLog(string.Format(UiStrings.Main_Validation_InvalidHostAddressFormat, trimmedHost));
                return;
            }

            if (row.Brand == PrinterBrand.Gainscha && row.GainschaLabelPreset is null)
            {
                AppendLog(UiStrings.Main_Validation_GainschaLabelPresetRequired);
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
                AppendLog(UiStrings.Main_DeployCancelledByMismatchWarning);
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
        IsDeployRunning = true;

        var progress = new SynchronousProgress<DeploymentProgressEvent>(e =>
        {
            RunOnUiDispatcher(() =>
            {
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

                var q = e.PrinterQueueName is null ? "—" : e.PrinterQueueName;
                AppendLog($"{e.ComputerName} [{q}]: {TargetMachineStateDisplay.GetDisplay(e.State)} — {e.Message}");
            });
        });

        var diagnosticProgress = new SynchronousProgress<string>(msg =>
        {
            RunOnUiDispatcher(() => AppendLog(msg));
        });

        try
        {
            await _orchestrator.RunAsync(request, journal, progress, _deployCts.Token, diagnosticProgress).ConfigureAwait(true);

            LastSummaryText = BuildSummaryText();
            NotifyDeployCompletion();
        }
        catch (OperationCanceledException)
        {
            AppendLog(UiStrings.Main_DeployCancelRequested);
            RunOnUiDispatcher(MarkIntermediateTargetsAsDeployCancelled);

            if (journal.HasRollbackWork)
            {
                AppendLog(UiStrings.Main_DeployRollbackStarting);
                var rbProgress = new SynchronousProgress<PrinterRemovalProgressEvent>(e =>
                {
                    RunOnUiDispatcher(() =>
                    {
                        ApplyRollbackProgress(e, journal);
                        AppendLog($"{e.ComputerName}: {e.Message}");
                    });
                });
                try
                {
                    await _rollbackRunner.RunAsync(journal, cred, rbProgress, CancellationToken.None).ConfigureAwait(true);
                    AppendLog(UiStrings.Main_DeployRollbackFinished);
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(UiStrings.Main_DeployRollbackErrorFormat, ex.Message));
                }
            }

            RunOnUiDispatcher(() =>
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

            AppendLog(UiStrings.Main_DeployCooperativeCancelHint);
            LastSummaryText = BuildSummaryText();
            _notificationService.NotifyWarning();
        }
        finally
        {
            _deployCts?.Dispose();
            _deployCts = null;
            IsDeployRunning = false;
        }

        if (!string.IsNullOrEmpty(LastSummaryText) && Application.Current is not null)
        {
            MessageBox.Show(LastSummaryText, UiStrings.Main_SummaryDialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool CanDeploy() => !IsDeployRunning;

    [RelayCommand(CanExecute = nameof(CanCancelDeploy))]
    private void CancelDeploy()
    {
        _deployCts?.Cancel();
    }

    private bool CanCancelDeploy() => IsDeployRunning;

    private void MarkIntermediateTargetsAsDeployCancelled()
    {
        foreach (var row in Targets.Where(r => IsIntermediateDeployState(r.State)))
        {
            row.State = TargetMachineState.DeployCancelled;
            row.Message = UiStrings.Main_DeployCancelledRowMessage;
        }
    }

    private static bool IsIntermediateDeployState(TargetMachineState s) =>
        s is TargetMachineState.ContactingRemote
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
            foreach (var t in Targets.Where(x => x.State is TargetMachineState.Error or TargetMachineState.AbortedDriverMissing))
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
        var errorCount = Targets.Count(t => t.State is TargetMachineState.Error or TargetMachineState.AbortedDriverMissing);
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

    private static void RunOnUiDispatcher(Action action)
    {
        if (Application.Current?.Dispatcher is not null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void AppendLog(string line)
    {
        void Write()
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LogText += $"[{ts}] {line}\r\n";
        }

        RunOnUiDispatcher(Write);
    }

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
