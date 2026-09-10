using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Logging;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.App.ViewModels;

public enum ComputerPingStatus
{
    None,
    Checking,
    Online,
    Offline
}

public partial class RemovalWizardViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly IRemotePrinterOperations _remote;
    private readonly PrinterControlOrchestrator _orchestrator;
    private readonly ILogExportService _logExportService;
    private readonly LocalMachineIdentity _localMachineIdentity;
    private readonly IDeploymentNotificationService _notificationService;
    private readonly IConfirmationDialogService _dialogService;
    private readonly IPrinterPingService _pingService;
    private readonly IFastHostReachabilityChecker _reachabilityChecker;

    private readonly Dictionary<string, List<PrinterRemovalQueueItem>> _selectionsByComputer = new();
    private readonly Dictionary<string, List<PrinterRenameItem>> _renamesByComputer = new();
    private List<string> _machineOrder = new();
    private int _machineIndex;
    private CancellationTokenSource? _loadCts;

    public RemovalWizardViewModel(
        ISessionContext session,
        IRemotePrinterOperations remote,
        PrinterControlOrchestrator orchestrator,
        ILogExportService? logExportService = null,
        LocalMachineIdentity? localMachineIdentity = null,
        IDeploymentNotificationService? notificationService = null,
        IConfirmationDialogService? dialogService = null,
        IPrinterPingService? pingService = null,
        IFastHostReachabilityChecker? reachabilityChecker = null)
    {
        _session = session;
        _remote = remote;
        _orchestrator = orchestrator;
        _logExportService = logExportService ?? new LogExportService();
        _localMachineIdentity = localMachineIdentity ?? new LocalMachineIdentity();
        _notificationService = notificationService ?? new DeploymentNotificationService();
        _dialogService = dialogService ?? new ConfirmationDialogService();
        _pingService = pingService ?? new NullPrinterPingService();
        _reachabilityChecker = reachabilityChecker ?? new NullFastHostReachabilityChecker();
        QueuesForCurrentComputer.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowQueuesEmptyHint));
    }

    [ObservableProperty] private int _currentStepIndex;

    [ObservableProperty] private string _computersText = "";

    [ObservableProperty] private string _currentComputerName = "";
    [ObservableProperty] private string _currentStepLabel = "";
    [ObservableProperty] private bool _isLoadingQueues;
    [ObservableProperty] private string? _queuesLoadError;

    [ObservableProperty] private ComputerPingStatus _pingStatus = ComputerPingStatus.None;
    [ObservableProperty] private string _loadingStatusMessage = "";
    [ObservableProperty] private bool _isComputerOffline;
    [ObservableProperty] private string _pingStatusBadgeText = "";

    public bool HasPingBadge => PingStatus != ComputerPingStatus.None;

    public string PingBadgeBackground => PingStatus switch
    {
        ComputerPingStatus.Checking => "#FFFDF3D8",
        ComputerPingStatus.Online => "#FFE6F4EA",
        ComputerPingStatus.Offline => "#FFFCE8E6",
        _ => "Transparent"
    };

    public string PingBadgeForeground => PingStatus switch
    {
        ComputerPingStatus.Checking => "#FF8F6B00",
        ComputerPingStatus.Online => "#FF137333",
        ComputerPingStatus.Offline => "#FFC5221F",
        _ => "#FF000000"
    };

    partial void OnPingStatusChanged(ComputerPingStatus value)
    {
        OnPropertyChanged(nameof(HasPingBadge));
        OnPropertyChanged(nameof(PingBadgeBackground));
        OnPropertyChanged(nameof(PingBadgeForeground));
    }

    partial void OnIsComputerOfflineChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowQueuesEmptyHint));
        OnPropertyChanged(nameof(ShowComputerOfflineHint));
        OnPropertyChanged(nameof(CanResetSpooler));
        ResetCurrentMachineSpoolerCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<SelectableQueueRow> QueuesForCurrentComputer { get; } = new();

    public bool ShowQueuesEmptyHint =>
        !IsLoadingQueues &&
        !IsComputerOffline &&
        string.IsNullOrEmpty(QueuesLoadError) &&
        QueuesForCurrentComputer.Count == 0;

    public bool ShowComputerOfflineHint =>
        !IsLoadingQueues &&
        IsComputerOffline;

    [ObservableProperty] private string _reviewSummary = "";

    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isExecuting;

    public bool CanExecute => CurrentStepIndex == 2 && !IsExecuting;

    public bool CanClose => CurrentStepIndex == 3 && !IsExecuting;

    public bool CanExportLog => CurrentStepIndex == 3 && !IsExecuting && !string.IsNullOrWhiteSpace(LogText);

    public event EventHandler? CloseRequested;

    [ObservableProperty] private bool _isResettingSpooler;

    partial void OnIsResettingSpoolerChanged(bool value)
    {
        OnPropertyChanged(nameof(CanResetSpooler));
        OnPropertyChanged(nameof(CanAdvanceQueueStep));
        OnPropertyChanged(nameof(CanRetryCurrentMachine));
        ResetCurrentMachineSpoolerCommand.NotifyCanExecuteChanged();
        NextQueueStepCommand.NotifyCanExecuteChanged();
        RetryCurrentMachineCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentComputerNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanResetSpooler));
        ResetCurrentMachineSpoolerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanExportLog));
        CloseCommand.NotifyCanExecuteChanged();
        ExportLogCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanExportLog));
        OnPropertyChanged(nameof(CanResetSpooler));
        OnPropertyChanged(nameof(CanAdvanceQueueStep));
        OnPropertyChanged(nameof(CanRetryCurrentMachine));
        CloseCommand.NotifyCanExecuteChanged();
        ExportLogCommand.NotifyCanExecuteChanged();
        ResetCurrentMachineSpoolerCommand.NotifyCanExecuteChanged();
        NextQueueStepCommand.NotifyCanExecuteChanged();
        RetryCurrentMachineCommand.NotifyCanExecuteChanged();
    }

    partial void OnLogTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanExportLog));
        ExportLogCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingQueuesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanResetSpooler));
        OnPropertyChanged(nameof(CanAdvanceQueueStep));
        OnPropertyChanged(nameof(CanRetryCurrentMachine));
        NextQueueStepCommand.NotifyCanExecuteChanged();
        ResetCurrentMachineSpoolerCommand.NotifyCanExecuteChanged();
        RetryCurrentMachineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowQueuesEmptyHint));
        OnPropertyChanged(nameof(ShowComputerOfflineHint));
    }

    partial void OnQueuesLoadErrorChanged(string? value) => OnPropertyChanged(nameof(ShowQueuesEmptyHint));

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
    private async Task StartAsync()
    {
        if (_session.Credential is null)
        {
            AppendLog(UiStrings.Removal_NotAuthenticated);
            return;
        }

        var names = ComputerNameListParser.Parse(ComputersText);
        if (names.Count == 0)
        {
            AppendLog(UiStrings.Removal_Validation_ComputersRequired);
            return;
        }

        _machineOrder = names.ToList();
        _selectionsByComputer.Clear();
        _renamesByComputer.Clear();
        _machineIndex = 0;
        CurrentStepIndex = 1;
        await LoadCurrentMachineAsync().ConfigureAwait(true);
    }

    public bool CanAdvanceQueueStep =>
        CurrentStepIndex == 1 &&
        !IsLoadingQueues &&
        !IsResettingSpooler;

    [RelayCommand(CanExecute = nameof(CanAdvanceQueueStep))]
    private async Task NextQueueStepAsync()
    {
        _loadCts?.Cancel();
        CaptureCurrentSelection();
        if (_machineIndex + 1 < _machineOrder.Count)
        {
            _machineIndex++;
            await LoadCurrentMachineAsync().ConfigureAwait(true);
            return;
        }

        BuildReviewSummary();
        CurrentStepIndex = 2;
    }

    public bool CanRetryCurrentMachine =>
        CurrentStepIndex == 1 &&
        !IsLoadingQueues &&
        !IsResettingSpooler;

    [RelayCommand(CanExecute = nameof(CanRetryCurrentMachine))]
    private async Task RetryCurrentMachineAsync()
    {
        await LoadCurrentMachineAsync().ConfigureAwait(true);
    }

    public bool CanResetSpooler =>
        CurrentStepIndex == 1 &&
        !IsResettingSpooler &&
        !IsLoadingQueues &&
        !IsComputerOffline &&
        !string.IsNullOrWhiteSpace(CurrentComputerName);

    [RelayCommand(CanExecute = nameof(CanResetSpooler))]
    private async Task ResetCurrentMachineSpoolerAsync()
    {
        if (_session.Credential is null)
        {
            AppendLog(UiStrings.Removal_NotAuthenticated);
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentComputerName))
            return;

        var confirmed = await _dialogService.ConfirmSpoolerResetAsync(CurrentComputerName).ConfigureAwait(true);
        if (!confirmed)
            return;

        IsResettingSpooler = true;
        AppendLog(string.Format(UiStrings.Removal_ResetSpoolerRunning, CurrentComputerName));

        try
        {
            var result = await _remote.ResetSpoolerServiceAsync(
                CurrentComputerName,
                _session.Credential,
                purgeJobs: true).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                AppendLog(string.Format(UiStrings.Removal_ResetSpoolerSuccess, CurrentComputerName));
                _notificationService.NotifySuccess();
                await LoadCurrentMachineAsync().ConfigureAwait(true);
            }
            else
            {
                AppendLog(string.Format(UiStrings.Removal_ResetSpoolerErrorFormat, CurrentComputerName, result.ErrorMessage));
                _notificationService.NotifyError();
            }
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(UiStrings.Removal_ResetSpoolerErrorFormat, CurrentComputerName, ex.Message));
            _notificationService.NotifyError();
        }
        finally
        {
            IsResettingSpooler = false;
        }
    }

    private static bool HasMeaningfulRename(SelectableQueueRow r)
    {
        var t = r.NewName?.Trim() ?? "";
        return t.Length > 0 && !string.Equals(t, r.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void OnQueueRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectableQueueRow.IsSelected) or nameof(SelectableQueueRow.NewName))
        {
            NextQueueStepCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (_session.Credential is null)
        {
            AppendLog(UiStrings.Removal_NotAuthenticated);
            return;
        }

        IsExecuting = true;
        CurrentStepIndex = 3;
        try
        {
            var targets = new List<PrinterControlTarget>();
            foreach (var computer in _machineOrder)
            {
                _selectionsByComputer.TryGetValue(computer, out var removes);
                _renamesByComputer.TryGetValue(computer, out var renames);
                removes ??= new List<PrinterRemovalQueueItem>();
                renames ??= new List<PrinterRenameItem>();
                if (removes.Count == 0 && renames.Count == 0)
                    continue;
                targets.Add(new PrinterControlTarget
                {
                    ComputerName = computer,
                    QueuesToRemove = removes,
                    Renames = renames
                });
            }

            if (targets.Count == 0)
            {
                AppendLog(UiStrings.Removal_NoPrintersSelected);
                return;
            }

            var request = new PrinterControlRequest
            {
                DomainCredential = _session.Credential,
                Targets = targets
            };

            var totalActions = targets.Sum(t => t.QueuesToRemove.Count + t.Renames.Count);
            var errorCount = 0;
            var warningCount = 0;
            var queueSuccessCount = 0;
            var failureLines = new List<string>();

            var progress = new SynchronousProgress<PrinterRemovalProgressEvent>(ev =>
            {
                AppendLog($"{ev.ComputerName}: {ev.State} - {ev.Message}");

                if (ev.State == PrinterRemovalProgressState.Error)
                {
                    errorCount++;
                    failureLines.Add($"{ev.ComputerName} [{ev.PrinterQueueName ?? "-"}] — {ev.Message}");
                }
                else if (ev.State == PrinterRemovalProgressState.Warning)
                {
                    warningCount++;
                }
                else if (ev.State == PrinterRemovalProgressState.RollbackSucceeded)
                {
                    queueSuccessCount++;
                }
            });

            await _orchestrator.RunAsync(request, progress).ConfigureAwait(true);
            AppendLog(UiStrings.Removal_Finished);

            var totalRenames = targets.Sum(t => t.Renames.Count);
            var renameErrors = failureLines.Count(f => targets.Any(t => t.Renames.Any(r => f.Contains(r.CurrentName, StringComparison.OrdinalIgnoreCase))));
            var successfulRenames = Math.Max(0, totalRenames - renameErrors);
            var effectiveSuccessCount = queueSuccessCount + successfulRenames;

            NotifyControlCompletion(totalActions, effectiveSuccessCount, warningCount, errorCount);

            var summary = BuildSummaryText(effectiveSuccessCount, warningCount, errorCount, failureLines);
            AppendLog(summary);

            if (Application.Current is not null)
            {
                var icon = errorCount > 0
                    ? (effectiveSuccessCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Error)
                    : (warningCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                MessageBox.Show(summary, UiStrings.Removal_SummaryDialogTitle, MessageBoxButton.OK, icon);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog(UiStrings.Removal_Cancelled);
            _notificationService.NotifyWarning();
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(UiStrings.Removal_LogErrorFormat, ex.Message));
            _notificationService.NotifyError();

            if (Application.Current is not null)
            {
                MessageBox.Show(
                    string.Format(UiStrings.Removal_LogErrorFormat, ex.Message),
                    UiStrings.Removal_SummaryDialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void NotifyControlCompletion(int totalActions, int successCount, int warningCount, int errorCount)
    {
        if (totalActions == 0)
            return;

        if (errorCount == 0 && warningCount == 0)
        {
            _notificationService.NotifySuccess();
        }
        else if (errorCount > 0 && successCount == 0 && errorCount >= totalActions)
        {
            _notificationService.NotifyError();
        }
        else
        {
            _notificationService.NotifyWarning();
        }
    }

    private static string BuildSummaryText(int successCount, int warningCount, int errorCount, IReadOnlyList<string> failureLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(UiStrings.Removal_SummaryLineFormat, successCount, warningCount, errorCount));
        if (failureLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(UiStrings.Removal_SummaryFailuresHeader);
            foreach (var line in failureLines)
            {
                sb.AppendLine(line);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private async Task LoadCurrentMachineAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoadingQueues = true;
        IsComputerOffline = false;
        QueuesLoadError = null;
        PingStatus = ComputerPingStatus.None;
        PingStatusBadgeText = string.Empty;

        foreach (var row in QueuesForCurrentComputer.ToList())
            row.PropertyChanged -= OnQueueRowPropertyChanged;
        QueuesForCurrentComputer.Clear();

        CurrentComputerName = _machineOrder[_machineIndex];
        CurrentStepLabel = string.Format(
            UiStrings.Removal_StepLabelFormat,
            _machineIndex + 1,
            _machineOrder.Count,
            CurrentComputerName);

        try
        {
            var isLocal = _localMachineIdentity.IsLocalMachine(CurrentComputerName);
            if (!isLocal)
            {
                PingStatus = ComputerPingStatus.Checking;
                PingStatusBadgeText = UiStrings.Removal_Ping_Checking;
                LoadingStatusMessage = string.Format(UiStrings.Removal_Ping_TestingHostFormat, CurrentComputerName);

                var isAlive = await CheckHostAliveAsync(CurrentComputerName, ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();

                if (!isAlive)
                {
                    PingStatus = ComputerPingStatus.Offline;
                    PingStatusBadgeText = UiStrings.Removal_Ping_Offline;
                    IsComputerOffline = true;
                    QueuesLoadError = string.Format(UiStrings.Removal_Ping_ComputerOfflineFormat, CurrentComputerName);
                    AppendLog(string.Format(UiStrings.Removal_LogListPrintersFailedFormat, CurrentComputerName, QueuesLoadError));
                    return;
                }
            }

            PingStatus = ComputerPingStatus.Online;
            PingStatusBadgeText = UiStrings.Removal_Ping_Online;
            LoadingStatusMessage = UiStrings.Removal_Ping_ConnectedLoadingPrinters;

            var cred = _session.Credential!;
            var list = await _remote.ListPrinterQueuesAsync(CurrentComputerName, cred, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            foreach (var q in list.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new SelectableQueueRow
                {
                    Name = q.Name,
                    PortName = q.PortName,
                    IsSelected = false
                };
                row.PropertyChanged += OnQueueRowPropertyChanged;
                QueuesForCurrentComputer.Add(row);
            }
        }
        catch (OperationCanceledException)
        {
            // Operação cancelada cooperativamente (ex: avanço, skip ou fechamento)
        }
        catch (Exception ex)
        {
            QueuesLoadError = ex.Message;
            AppendLog(string.Format(UiStrings.Removal_LogListPrintersFailedFormat, CurrentComputerName, ex.Message));
        }
        finally
        {
            IsLoadingQueues = false;
        }
    }

    private async Task<bool> CheckHostAliveAsync(string host, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pingTask = _pingService.PingAsync(host, TimeSpan.FromSeconds(2), linkedCts.Token);
        var reachTask = _reachabilityChecker.CheckReachabilityAsync(host, linkedCts.Token);

        var tasks = new List<Task> { pingTask, reachTask };
        while (tasks.Count > 0)
        {
            var finished = await Task.WhenAny(tasks).ConfigureAwait(true);
            tasks.Remove(finished);

            if (finished == pingTask)
            {
                try
                {
                    var ok = await pingTask.ConfigureAwait(true);
                    if (ok)
                    {
                        linkedCts.Cancel();
                        return true;
                    }
                }
                catch
                {
                    // Falha no ICMP; aguarda o reachTask
                }
            }
            else if (finished == reachTask)
            {
                try
                {
                    var (ok, _) = await reachTask.ConfigureAwait(true);
                    if (ok)
                    {
                        linkedCts.Cancel();
                        return true;
                    }
                }
                catch
                {
                    // Falha no TCP 135/445; aguarda o pingTask
                }
            }
        }

        return false;
    }

    private void CaptureCurrentSelection()
    {
        var chosen = QueuesForCurrentComputer
            .Where(r => r.IsSelected)
            .Select(r => new PrinterRemovalQueueItem(r.Name, r.PortName))
            .ToList();
        _selectionsByComputer[CurrentComputerName] = chosen;

        var renames = QueuesForCurrentComputer
            .Where(HasMeaningfulRename)
            .Select(r => new PrinterRenameItem(r.Name, r.NewName.Trim()))
            .ToList();
        _renamesByComputer[CurrentComputerName] = renames;
    }

    private void BuildReviewSummary()
    {
        var lines = new List<string>();
        foreach (var computer in _machineOrder)
        {
            _selectionsByComputer.TryGetValue(computer, out var queues);
            _renamesByComputer.TryGetValue(computer, out var renames);
            queues ??= new List<PrinterRemovalQueueItem>();
            renames ??= new List<PrinterRenameItem>();
            if (queues.Count == 0 && renames.Count == 0)
            {
                lines.Add(string.Format(UiStrings.Removal_ReviewNothingFormat, computer));
                continue;
            }
            foreach (var rename in renames)
            {
                lines.Add(string.Format(
                    UiStrings.Removal_ReviewRenameFormat,
                    computer,
                    rename.CurrentName,
                    rename.NewName));
            }
            foreach (var q in queues)
            {
                lines.Add(string.Format(
                    UiStrings.Removal_ReviewRemoveFormat,
                    computer,
                    q.PrinterName,
                    q.PortName ?? "-"));
            }
        }
        ReviewSummary = string.Join(Environment.NewLine, lines);
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

        var report = LogReportFormatter.FormatRemovalReport(
            operatorId,
            Environment.MachineName,
            ReviewSummary,
            LogText);

        var defaultFileName = $"PrinterInstall_Controle_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        var result = _logExportService.ExportLog(defaultFileName, report);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.FilePath))
        {
            AppendLog(string.Format(UiStrings.Removal_LogExportSuccessFormat, result.FilePath));
        }
        else if (!result.IsCancelled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AppendLog(string.Format(UiStrings.Removal_LogExportErrorFormat, result.ErrorMessage));
        }
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _loadCts?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AppendLog(string line)
    {
        void Write()
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LogText += $"[{ts}] {line}\r\n";
        }

        if (Application.Current?.Dispatcher is not null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(Write);
        }
        else
        {
            Write();
        }
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value) => _handler(value);
    }
}
