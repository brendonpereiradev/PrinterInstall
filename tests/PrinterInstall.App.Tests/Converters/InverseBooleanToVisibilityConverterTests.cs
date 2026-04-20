using System.Globalization;
using System.Windows;
using PrinterInstall.App.Converters;

namespace PrinterInstall.App.Tests.Converters;

public class InverseBooleanToVisibilityConverterTests
{
    private readonly InverseBooleanToVisibilityConverter _sut = new();

    [Fact]
    public void Convert_true_returns_Collapsed()
    {
        Assert.Equal(Visibility.Collapsed, (Visibility)_sut.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void Convert_false_returns_Visible()
    {
        Assert.Equal(Visibility.Visible, (Visibility)_sut.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture)!);
    }
}
