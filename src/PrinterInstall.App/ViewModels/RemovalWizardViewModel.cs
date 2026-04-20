using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.App.ViewModels;

public partial class RemovalWizardViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly IRemotePrinterOperations _remote;
    private readonly PrinterRemovalOrchestrator _orchestrator;

    private readonly Dictionary<string, List<PrinterRemovalQueueItem>> _selectionsByComputer = new();
    private List<string> _machineOrder = new();
    private int _machineIndex;

    public RemovalWizardViewModel(
        ISessionContext session,
        IRemotePrinterOperations remote,
        PrinterRemovalOrchestrator orchestrator)
    {
        _session = session;
        _remote = remote;
        _orchestrator = orchestrator;
    }

    [ObservableProperty] private int _currentStepIndex;

    [ObservableProperty] private string _computersText = "";

    [ObservableProperty] private string _currentComputerName = "";
    [ObservableProperty] private string _currentStepLabel = "";
    [ObservableProperty] private bool _isLoadingQueues;
    [ObservableProperty] private string? _queuesLoadError;

    public ObservableCollection<SelectableQueueRow> QueuesForCurrentComputer { get; } = new();

    [ObservableProperty] private string _reviewSummary = "";

    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isExecuting;

    public bool CanExecute => CurrentStepIndex == 2 && !IsExecuting;

    partial void OnIsExecutingChanged(bool value) => OnPropertyChanged(nameof(CanExecute));
    partial void OnCurrentStepIndexChanged(int value) => OnPropertyChanged(nameof(CanExecute));

    partial void OnIsLoadingQueuesChanged(bool value) => NextQueueStepCommand.NotifyCanExecuteChanged();

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
        _machineIndex = 0;
        CurrentStepIndex = 1;
        await LoadCurrentMachineAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanAdvanceQueueStep))]
    private async Task NextQueueStepAsync()
    {
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

    private bool CanAdvanceQueueStep()
    {
        if (IsLoadingQueues)
            return false;
        if (QueuesForCurrentComputer.Count == 0)
            return true;
        return QueuesForCurrentComputer.Any(r => r.IsSelected);
    }

    private void OnQueueRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableQueueRow.IsSelected))
            NextQueueStepCommand.NotifyCanExecuteChanged();
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
            var targets = _selectionsByComputer
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => new PrinterRemovalTarget { ComputerName = kv.Key, QueuesToRemove = kv.Value })
                .ToList();

            if (targets.Count == 0)
            {
                AppendLog(UiStrings.Removal_NoPrintersSelected);
                return;
            }

            var request = new PrinterRemovalRequest
            {
                DomainCredential = _session.Credential,
                Targets = targets
            };

            var progress = new Progress<PrinterRemovalProgressEvent>(ev =>
                AppendLog($"{ev.ComputerName}: {ev.State} - {ev.Message}"));

            await _orchestrator.RunAsync(request, progress).ConfigureAwait(true);
            AppendLog(UiStrings.Removal_Finished);
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(UiStrings.Removal_LogErrorFormat, ex.Message));
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task LoadCurrentMachineAsync()
    {
        IsLoadingQueues = true;
        NextQueueStepCommand.NotifyCanExecuteChanged();

        foreach (var row in QueuesForCurrentComputer.ToList())
            row.PropertyChanged -= OnQueueRowPropertyChanged;
        QueuesForCurrentComputer.Clear();

        QueuesLoadError = null;
        CurrentComputerName = _machineOrder[_machineIndex];
        CurrentStepLabel = string.Format(
            UiStrings.Removal_StepLabelFormat,
            _machineIndex + 1,
            _machineOrder.Count,
            CurrentComputerName);
        try
        {
            var cred = _session.Credential!;
            var list = await _remote.ListPrinterQueuesAsync(CurrentComputerName, cred).ConfigureAwait(true);
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
        catch (Exception ex)
        {
            QueuesLoadError = ex.Message;
            AppendLog(string.Format(UiStrings.Removal_LogListPrintersFailedFormat, CurrentComputerName, ex.Message));
        }
        finally
        {
            IsLoadingQueues = false;
            NextQueueStepCommand.NotifyCanExecuteChanged();
        }
    }

    private void CaptureCurrentSelection()
    {
        var chosen = QueuesForCurrentComputer
            .Where(r => r.IsSelected)
            .Select(r => new PrinterRemovalQueueItem(r.Name, r.PortName))
            .ToList();
        _selectionsByComputer[CurrentComputerName] = chosen;
    }

    private void BuildReviewSummary()
    {
        var lines = new List<string>();
        foreach (var computer in _machineOrder)
        {
            if (!_selectionsByComputer.TryGetValue(computer, out var queues) || queues.Count == 0)
            {
                lines.Add(string.Format(UiStrings.Removal_ReviewNothingFormat, computer));
                continue;
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

    private void AppendLog(string line)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LogText += $"[{ts}] {line}\r\n";
        });
    }
}
