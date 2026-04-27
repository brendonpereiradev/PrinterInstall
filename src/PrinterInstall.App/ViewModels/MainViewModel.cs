using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PrinterInstall.App.Localization;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int DefaultDeployPort = 9100;
    private const TcpPrinterProtocol DefaultDeployProtocol = TcpPrinterProtocol.Raw;

    private readonly ISessionContext _session;
    private readonly PrinterDeploymentOrchestrator _orchestrator;
    private readonly IServiceProvider _serviceProvider;

    public MainViewModel(ISessionContext session, PrinterDeploymentOrchestrator orchestrator, IServiceProvider serviceProvider)
    {
        _session = session;
        _orchestrator = orchestrator;
        _serviceProvider = serviceProvider;
        PrinterRows.Add(new PrinterFormRowViewModel());
        Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatusEmptyHint));
    }

    public bool ShowStatusEmptyHint => Targets.Count == 0;

    [ObservableProperty]
    private string _computersText = "";

    [ObservableProperty]
    private bool _printTestPage;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _lastSummaryText = "";

    public ObservableCollection<PrinterFormRowViewModel> PrinterRows { get; } = new();

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    [RelayCommand]
    private void AddPrinterRow()
    {
        PrinterRows.Add(new PrinterFormRowViewModel());
    }

    [RelayCommand]
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

    [RelayCommand]
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
            return;
        }

        var definitions = new List<PrinterQueueDefinition>();
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

            definitions.Add(new PrinterQueueDefinition
            {
                Brand = row.Brand,
                DisplayName = row.DisplayName.Trim(),
                PrinterHostAddress = row.PrinterHostAddress.Trim(),
                PortNumber = DefaultDeployPort,
                Protocol = DefaultDeployProtocol
            });
        }

        var validNames = new List<string>();
        foreach (var n in rawNames)
        {
            if (!ComputerNameValidator.IsPlausibleComputerName(n))
            {
                foreach (var def in definitions)
                {
                    Targets.Add(new TargetRowViewModel
                    {
                        ComputerName = n,
                        PrinterQueueName = def.DisplayName,
                        State = TargetMachineState.Error,
                        Message = UiStrings.Main_InvalidComputerNameFormat
                    });
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
                Targets.Add(new TargetRowViewModel
                {
                    ComputerName = n,
                    PrinterQueueName = def.DisplayName,
                    State = TargetMachineState.Pending
                });
            }
        }

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = validNames,
            Printers = definitions,
            DomainCredential = cred,
            PrintTestPage = PrintTestPage
        };

        var progress = new Progress<DeploymentProgressEvent>(e =>
        {
            Application.Current.Dispatcher.Invoke(() =>
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

        await _orchestrator.RunAsync(request, progress).ConfigureAwait(true);

        LastSummaryText = BuildSummaryText();
        if (!string.IsNullOrEmpty(LastSummaryText))
        {
            MessageBox.Show(LastSummaryText, UiStrings.Main_SummaryDialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string BuildSummaryText()
    {
        if (Targets.Count == 0)
            return string.Empty;

        var ok = 0;
        var skipped = 0;
        var err = 0;
        var aborted = 0;
        var other = 0;
        foreach (var t in Targets)
        {
            switch (t.State)
            {
                case TargetMachineState.CompletedSuccess: ok++; break;
                case TargetMachineState.SkippedAlreadyExists: skipped++; break;
                case TargetMachineState.Error: err++; break;
                case TargetMachineState.AbortedDriverMissing: aborted++; break;
                default: other++; break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(UiStrings.Main_SummaryLineFormat, ok, skipped, err, aborted));
        if (other > 0)
            sb.AppendLine(string.Format(UiStrings.Main_SummaryOtherFormat, other));

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

    private void AppendLog(string line)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        LogText += $"[{ts}] {line}\r\n";
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
}
