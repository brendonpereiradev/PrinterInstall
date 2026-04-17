using CommunityToolkit.Mvvm.ComponentModel;

namespace PrinterInstall.App.ViewModels;

public partial class SelectableQueueRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _portName;
    [ObservableProperty] private bool _isSelected;
}
