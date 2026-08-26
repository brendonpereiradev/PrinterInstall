using System.Globalization;
using System.Windows;
using PrinterInstall.App.Converters;

namespace PrinterInstall.App.Tests.Converters;

/// <summary>
/// Testes unitários para <see cref="StringNullOrWhiteSpaceToVisibilityConverter"/>.
/// </summary>
public class StringNullOrWhiteSpaceToVisibilityConverterTests
{
    private readonly StringNullOrWhiteSpaceToVisibilityConverter _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n   ")]
    public void Convert_NullOrWhiteSpaceWithoutInvert_ReturnsVisible(string? value)
    {
        // Arrange & Act
        var result = _sut.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    [Theory]
    [InlineData("Texto")]
    [InlineData("  Texto com espaços  ")]
    [InlineData("a")]
    public void Convert_NonEmptyStringWithoutInvert_ReturnsCollapsed(string value)
    {
        // Arrange & Act
        var result = _sut.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Theory]
    [InlineData(null, "Invert")]
    [InlineData("", "Invert")]
    [InlineData("   ", "Invert")]
    [InlineData("", "invert")]
    [InlineData(null, "INVERT")]
    public void Convert_NullOrWhiteSpaceWithInvert_ReturnsCollapsed(string? value, string parameter)
    {
        // Arrange & Act
        var result = _sut.Convert(value, typeof(Visibility), parameter, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Theory]
    [InlineData("Texto", "Invert")]
    [InlineData("  Texto com espaços  ", "Invert")]
    [InlineData("a", "invert")]
    [InlineData("123", "INVERT")]
    public void Convert_NonEmptyStringWithInvert_ReturnsVisible(string value, string parameter)
    {
        // Arrange & Act
        var result = _sut.Convert(value, typeof(Visibility), parameter, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NonStringValueWithoutInvert_ReturnsVisible()
    {
        // Arrange & Act (objeto não string é tratado como null)
        var result = _sut.Convert(12345, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NonStringValueWithInvert_ReturnsCollapsed()
    {
        // Arrange & Act
        var result = _sut.Convert(12345, typeof(Visibility), "Invert", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange & Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            _sut.ConvertBack(Visibility.Visible, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
