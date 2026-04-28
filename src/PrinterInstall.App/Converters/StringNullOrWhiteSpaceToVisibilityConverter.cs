using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrinterInstall.App.Converters;

public sealed class StringNullOrWhiteSpaceToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var isEmpty = string.IsNullOrWhiteSpace(value as string);
        var show = invert ? !isEmpty : isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
