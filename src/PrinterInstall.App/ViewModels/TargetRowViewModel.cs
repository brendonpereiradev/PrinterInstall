using CommunityToolkit.Mvvm.ComponentModel;
using PrinterInstall.App.Localization;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.ViewModels;

public partial class TargetRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _computerName = "";

    [ObservableProperty]
    private string _printerQueueName = "";

    [ObservableProperty]
    private TargetMachineState _state = TargetMachineState.Pending;

    [ObservableProperty]
    private string _stateDisplay = TargetMachineStateDisplay.GetDisplay(TargetMachineState.Pending);

    [ObservableProperty]
    private string _message = "";

    partial void OnStateChanged(TargetMachineState value) =>
        StateDisplay = TargetMachineStateDisplay.GetDisplay(value);
}
