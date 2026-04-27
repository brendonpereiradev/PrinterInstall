using CommunityToolkit.Mvvm.ComponentModel;

namespace PrinterInstall.App.ViewModels;

public partial class SelectableQueueRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _portName;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _newName = "";

    /// <summary>When <c>true</c>, the "New name" text box is enabled (mutually exclusive with <see cref="IsSelected"/> for removal).</summary>
    public bool IsRenameEditable => !IsSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            NewName = string.Empty;
        OnPropertyChanged(nameof(IsRenameEditable));
    }

    partial void OnNewNameChanged(string value)
    {
        var t = value?.Trim() ?? "";
        if (t.Length > 0 && !string.Equals(t, Name, StringComparison.OrdinalIgnoreCase))
            IsSelected = false;
        OnPropertyChanged(nameof(IsRenameEditable));
    }
}
