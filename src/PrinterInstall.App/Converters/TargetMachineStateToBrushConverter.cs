using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Converters;

public sealed class TargetMachineStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PendingBackground = CreateBrush("#FFE9EEF6");
    private static readonly SolidColorBrush PendingBorder = CreateBrush("#FF9DB0C8");
    private static readonly SolidColorBrush PendingForeground = CreateBrush("#FF2E4A6E");

    private static readonly SolidColorBrush ActiveBackground = CreateBrush("#FFE5F2FF");
    private static readonly SolidColorBrush ActiveBorder = CreateBrush("#FF7AB6F0");
    private static readonly SolidColorBrush ActiveForeground = CreateBrush("#FF0C4A8A");

    private static readonly SolidColorBrush SuccessBackground = CreateBrush("#FFE7F6EC");
    private static readonly SolidColorBrush SuccessBorder = CreateBrush("#FF87C79B");
    private static readonly SolidColorBrush SuccessForeground = CreateBrush("#FF1F6B35");

    private static readonly SolidColorBrush WarningBackground = CreateBrush("#FFFFF4E5");
    private static readonly SolidColorBrush WarningBorder = CreateBrush("#FFE2BD7A");
    private static readonly SolidColorBrush WarningForeground = CreateBrush("#FF8A5A00");

    private static readonly SolidColorBrush ErrorBackground = CreateBrush("#FFFCE8E8");
    private static readonly SolidColorBrush ErrorBorder = CreateBrush("#FFE59A9A");
    private static readonly SolidColorBrush ErrorForeground = CreateBrush("#FF8C1D1D");

    private static readonly SolidColorBrush RolledBackBackground = CreateBrush("#FFE0F2F1");
    private static readonly SolidColorBrush RolledBackBorder = CreateBrush("#FF4DB6AC");
    private static readonly SolidColorBrush RolledBackForeground = CreateBrush("#FF004D40");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TargetMachineState s ? s : TargetMachineState.Pending;
        var role = parameter?.ToString();

        return role switch
        {
            "Border" => GetBorderBrush(state),
            "Foreground" => GetForegroundBrush(state),
            _ => GetBackgroundBrush(state)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush GetBackgroundBrush(TargetMachineState state) => state switch
    {
        TargetMachineState.CompletedSuccess => SuccessBackground,
        TargetMachineState.SkippedAlreadyExists => WarningBackground,
        TargetMachineState.AbortedDriverMissing => ErrorBackground,
        TargetMachineState.Error => ErrorBackground,
        TargetMachineState.ContactingRemote => ActiveBackground,
        TargetMachineState.ValidatingDriver => ActiveBackground,
        TargetMachineState.InstallingDriver => ActiveBackground,
        TargetMachineState.DriverInstalledReconfirming => ActiveBackground,
        TargetMachineState.Configuring => ActiveBackground,
        TargetMachineState.DeployCancelled => WarningBackground,
        TargetMachineState.RollbackRemovingQueue => ActiveBackground,
        TargetMachineState.RollbackRemovingPort => ActiveBackground,
        TargetMachineState.RolledBack => RolledBackBackground,
        _ => PendingBackground
    };

    private static Brush GetBorderBrush(TargetMachineState state) => state switch
    {
        TargetMachineState.CompletedSuccess => SuccessBorder,
        TargetMachineState.SkippedAlreadyExists => WarningBorder,
        TargetMachineState.AbortedDriverMissing => ErrorBorder,
        TargetMachineState.Error => ErrorBorder,
        TargetMachineState.ContactingRemote => ActiveBorder,
        TargetMachineState.ValidatingDriver => ActiveBorder,
        TargetMachineState.InstallingDriver => ActiveBorder,
        TargetMachineState.DriverInstalledReconfirming => ActiveBorder,
        TargetMachineState.Configuring => ActiveBorder,
        TargetMachineState.DeployCancelled => WarningBorder,
        TargetMachineState.RollbackRemovingQueue => ActiveBorder,
        TargetMachineState.RollbackRemovingPort => ActiveBorder,
        TargetMachineState.RolledBack => RolledBackBorder,
        _ => PendingBorder
    };

    private static Brush GetForegroundBrush(TargetMachineState state) => state switch
    {
        TargetMachineState.CompletedSuccess => SuccessForeground,
        TargetMachineState.SkippedAlreadyExists => WarningForeground,
        TargetMachineState.AbortedDriverMissing => ErrorForeground,
        TargetMachineState.Error => ErrorForeground,
        TargetMachineState.ContactingRemote => ActiveForeground,
        TargetMachineState.ValidatingDriver => ActiveForeground,
        TargetMachineState.InstallingDriver => ActiveForeground,
        TargetMachineState.DriverInstalledReconfirming => ActiveForeground,
        TargetMachineState.Configuring => ActiveForeground,
        TargetMachineState.DeployCancelled => WarningForeground,
        TargetMachineState.RollbackRemovingQueue => ActiveForeground,
        TargetMachineState.RollbackRemovingPort => ActiveForeground,
        TargetMachineState.RolledBack => RolledBackForeground,
        _ => PendingForeground
    };

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();
        return brush;
    }
}
