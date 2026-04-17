using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Catalog;
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
        _selectedModelId = PrinterCatalog.GetModels(PrinterBrand.Epson)[0].Id;
    }

    [ObservableProperty]
    private string _computersText = "";

    [ObservableProperty]
    private PrinterBrand _selectedBrand;

    [ObservableProperty]
    private string _selectedModelId;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _printerHostAddress = "";

    [ObservableProperty]
    private string _logText = "";

    private const int DefaultPortNumber = 9100;
    private const TcpPrinterProtocol DefaultProtocol = TcpPrinterProtocol.Raw;

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    public IEnumerable<PrinterBrand> Brands => Enum.GetValues<PrinterBrand>();

    public IReadOnlyList<PrinterModelOption> ModelsForBrand => PrinterCatalog.GetModels(SelectedBrand);

    partial void OnSelectedBrandChanged(PrinterBrand value)
    {
        var models = PrinterCatalog.GetModels(value);
        SelectedModelId = models[0].Id;
        OnPropertyChanged(nameof(ModelsForBrand));
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        LogText = "";
        Targets.Clear();

        var cred = _session.Credential;
        if (cred == null)
        {
            AppendLog("Not authenticated.");
            return;
        }

        var rawNames = ComputerNameListParser.Parse(ComputersText);
        if (rawNames.Count == 0)
        {
            AppendLog("Enter at least one computer name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            AppendLog("Display name is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PrinterHostAddress))
        {
            AppendLog("Printer host address is required.");
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
                    Message = "Invalid computer name format"
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
            SelectedModelId = SelectedModelId,
            DisplayName = DisplayName.Trim(),
            PrinterHostAddress = PrinterHostAddress.Trim(),
            PortNumber = DefaultPortNumber,
            Protocol = DefaultProtocol,
            DomainCredential = cred
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

                AppendLog($"{e.ComputerName}: {e.State} — {e.Message}");
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
