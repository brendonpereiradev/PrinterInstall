using System.Globalization;
using System.Windows.Data;

namespace PrinterInstall.App.Converters;

/// <summary>
/// Converte um valor booleano para o seu inverso lógico.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}
