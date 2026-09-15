using System.Globalization;
using PrinterInstall.App.Converters;

namespace PrinterInstall.App.Tests.Converters;

public class InverseBooleanConverterTests
{
    private readonly InverseBooleanConverter _sut = new();

    [Fact]
    public void Convert_true_returns_false()
    {
        Assert.Equal(false, _sut.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_false_returns_true()
    {
        Assert.Equal(true, _sut.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_non_bool_returns_false()
    {
        Assert.Equal(false, _sut.Convert("invalid", typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_true_returns_false()
    {
        Assert.Equal(false, _sut.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_false_returns_true()
    {
        Assert.Equal(true, _sut.ConvertBack(false, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
