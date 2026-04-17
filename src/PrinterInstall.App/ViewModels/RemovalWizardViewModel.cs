using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_session.Credential is null)
        {
            AppendLog("Not authenticated.");
            return;
        }

        var names = ComputerNameListParser.Parse(ComputersText);
        if (names.Count == 0)
        {
            AppendLog("Enter at least one computer name.");
            return;
        }

        _machineOrder = names.ToList();
        _selectionsByComputer.Clear();
        _machineIndex = 0;
        CurrentStepIndex = 1;
        await LoadCurrentMachineAsync().ConfigureAwait(true);
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (_session.Credential is null)
        {
            AppendLog("Not authenticated.");
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
                AppendLog("No printers selected; nothing to do.");
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
            AppendLog("Removal finished.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private async Task LoadCurrentMachineAsync()
    {
        QueuesForCurrentComputer.Clear();
        QueuesLoadError = null;
        CurrentComputerName = _machineOrder[_machineIndex];
        CurrentStepLabel = $"Computer {_machineIndex + 1} of {_machineOrder.Count}: {CurrentComputerName}";
        IsLoadingQueues = true;
        try
        {
            var cred = _session.Credential!;
            var list = await _remote.ListPrinterQueuesAsync(CurrentComputerName, cred).ConfigureAwait(true);
            foreach (var q in list.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
            {
                QueuesForCurrentComputer.Add(new SelectableQueueRow
                {
                    Name = q.Name,
                    PortName = q.PortName,
                    IsSelected = false
                });
            }
        }
        catch (Exception ex)
        {
            QueuesLoadError = ex.Message;
            AppendLog($"{CurrentComputerName}: failed to list printers - {ex.Message}");
        }
        finally
        {
            IsLoadingQueues = false;
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
                lines.Add($"{computer}: (nothing to remove)");
                continue;
            }
            foreach (var q in queues)
            {
                lines.Add($"{computer}: remove '{q.PrinterName}' (port '{q.PortName ?? "-"}')");
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
