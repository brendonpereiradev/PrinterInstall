using System.Globalization;
using System.Windows.Data;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Converters;

public sealed class TargetMachineStateToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TargetMachineState s ? s : TargetMachineState.Pending;
        return state switch
        {
            TargetMachineState.CompletedSuccess => "\uE73E",
            TargetMachineState.SkippedAlreadyExists => "\uE946",
            TargetMachineState.AbortedDriverMissing => "\uE711",
            TargetMachineState.Error => "\uEA39",
            TargetMachineState.ContactingRemote => "\uE895",
            TargetMachineState.ValidatingDriver => "\uE9D9",
            TargetMachineState.InstallingDriver => "\uE898",
            TargetMachineState.DriverInstalledReconfirming => "\uE895",
            TargetMachineState.Configuring => "\uE9F5",
            _ => "\uE823"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
