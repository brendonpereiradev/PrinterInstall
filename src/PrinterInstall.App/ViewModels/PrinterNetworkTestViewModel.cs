using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.App.ViewModels;

public partial class PrinterNetworkTestViewModel : ObservableObject
{
    private readonly IDirectRawPrinterTestService _testService;
    private readonly IConfirmationDialogService _dialogService;
    private CancellationTokenSource? _cts;

    public PrinterNetworkTestViewModel(
        IDirectRawPrinterTestService testService,
        IConfirmationDialogService? dialogService = null)
    {
        _testService = testService;
        _dialogService = dialogService ?? new ConfirmationDialogService();
    }

    [ObservableProperty] private PrinterBrand _selectedBrand = PrinterBrand.Epson;
    [ObservableProperty] private string _hostAddress = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private GainschaLabelPreset _selectedGainschaLabelPreset = GainschaLabelPreset.Paciente;

    public IEnumerable<PrinterBrand> BrandChoices => Enum.GetValues<PrinterBrand>();

    public IEnumerable<GainschaLabelPreset> GainschaLabelPresetChoices =>
        GainschaLabelPresetCatalog.NetworkTestDisplayOrder;

    public bool IsGainschaBrand => SelectedBrand == PrinterBrand.Gainscha;

    public bool CanRun => !IsRunning && !string.IsNullOrWhiteSpace(HostAddress);

    partial void OnHostAddressChanged(string value) => RunTestCommand.NotifyCanExecuteChanged();

    partial void OnSelectedBrandChanged(PrinterBrand value)
    {
        OnPropertyChanged(nameof(IsGainschaBrand));
        RunTestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
        RunTestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(HostAddress))
        {
            StatusMessage = UiStrings.NetworkTest_Validation_HostRequired;
            return;
        }

        var gainschaPreset = SelectedBrand == PrinterBrand.Gainscha
            ? SelectedGainschaLabelPreset
            : (GainschaLabelPreset?)null;

        var confirmed = await _dialogService.ConfirmNetworkTestAsync(
            HostAddress.Trim(),
            SelectedBrand,
            gainschaPreset);

        if (!confirmed)
        {
            StatusMessage = UiStrings.NetworkTest_Cancelled;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = UiStrings.NetworkTest_Progress_Connectivity;

        try
        {
            await Task.Yield();
            StatusMessage = UiStrings.NetworkTest_Progress_Sending;
            var result = await _testService.RunAsync(
                HostAddress.Trim(),
                SelectedBrand,
                gainschaPreset,
                _cts.Token).ConfigureAwait(true);
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiStrings.NetworkTest_Cancelled;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        _cts?.Cancel();
    }
}
