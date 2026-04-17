using CommunityToolkit.Mvvm.ComponentModel;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.ViewModels;

public partial class TargetRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _computerName = "";

    [ObservableProperty]
    private TargetMachineState _state = TargetMachineState.Pending;

    [ObservableProperty]
    private string _message = "";
}
