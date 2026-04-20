using System.Collections.ObjectModel;
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
    private readonly ISessionContext _session;
    private readonly PrinterDeploymentOrchestrator _orchestrator;
    private readonly IServiceProvider _serviceProvider;

    public MainViewModel(ISessionContext session, PrinterDeploymentOrchestrator orchestrator, IServiceProvider serviceProvider)
    {
        _session = session;
        _orchestrator = orchestrator;
        _serviceProvider = serviceProvider;
        _selectedBrand = PrinterBrand.Epson;
        Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatusEmptyHint));
    }

    public bool ShowStatusEmptyHint => Targets.Count == 0;

    [ObservableProperty]
    private string _computersText = "";

    [ObservableProperty]
    private PrinterBrand _selectedBrand;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _printerHostAddress = "";

    [ObservableProperty]
    private bool _printTestPage;

    [ObservableProperty]
    private string _logText = "";

    private const int DefaultPortNumber = 9100;
    private const TcpPrinterProtocol DefaultProtocol = TcpPrinterProtocol.Raw;

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    public IEnumerable<PrinterBrand> Brands => Enum.GetValues<PrinterBrand>();

    [RelayCommand]
    private async Task DeployAsync()
    {
        LogText = "";
        Targets.Clear();

        var cred = _session.Credential;
        if (cred == null)
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

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            AppendLog(UiStrings.Main_Validation_DisplayNameRequired);
            return;
        }

        if (string.IsNullOrWhiteSpace(PrinterHostAddress))
        {
            AppendLog(UiStrings.Main_Validation_PrinterHostRequired);
            return;
        }

        var validNames = new List<string>();
        foreach (var n in rawNames)
        {
            if (!ComputerNameValidator.IsPlausibleComputerName(n))
            {
                Targets.Add(new TargetRowViewModel
                {
                    ComputerName = n,
                    State = TargetMachineState.Error,
                    Message = UiStrings.Main_InvalidComputerNameFormat
                });
                continue;
            }

            validNames.Add(n);
            Targets.Add(new TargetRowViewModel { ComputerName = n, State = TargetMachineState.Pending });
        }

        if (validNames.Count == 0)
            return;

        var request = new PrinterDeploymentRequest
        {
            TargetComputerNames = validNames,
            Brand = SelectedBrand,
            DisplayName = DisplayName.Trim(),
            PrinterHostAddress = PrinterHostAddress.Trim(),
            PortNumber = DefaultPortNumber,
            Protocol = DefaultProtocol,
            DomainCredential = cred,
            PrintTestPage = PrintTestPage
        };

        var progress = new Progress<DeploymentProgressEvent>(e =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var row = Targets.FirstOrDefault(t => t.ComputerName == e.ComputerName);
                if (row != null)
                {
                    row.State = e.State;
                    row.Message = e.Message;
                }

                AppendLog($"{e.ComputerName}: {TargetMachineStateDisplay.GetDisplay(e.State)} — {e.Message}");
            });
        });

        await _orchestrator.RunAsync(request, progress).ConfigureAwait(true);
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
        if (owner != null)
            window.Owner = owner;
        window.ShowDialog();
    }
}
