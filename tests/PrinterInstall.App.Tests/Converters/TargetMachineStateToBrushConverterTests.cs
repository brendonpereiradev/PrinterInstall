using System.Globalization;
using System.Windows.Media;
using PrinterInstall.App.Converters;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Tests.Converters;

/// <summary>
/// Testes unitários para <see cref="TargetMachineStateToBrushConverter"/>.
/// </summary>
public class TargetMachineStateToBrushConverterTests
{
    private readonly TargetMachineStateToBrushConverter _sut = new();

    public static IEnumerable<object[]> AllTargetMachineStates =>
        Enum.GetValues<TargetMachineState>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(AllTargetMachineStates))]
    public void Convert_BackgroundRole_ReturnsNonNullFrozenBrush(TargetMachineState state)
    {
        // Arrange & Act
        var result = _sut.Convert(state, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        Assert.True(brush.IsFrozen);
        Assert.NotNull(brush);
    }

    [Theory]
    [MemberData(nameof(AllTargetMachineStates))]
    public void Convert_BorderRole_ReturnsNonNullFrozenBrush(TargetMachineState state)
    {
        // Arrange & Act
        var result = _sut.Convert(state, typeof(Brush), "Border", CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        Assert.True(brush.IsFrozen);
        Assert.NotNull(brush);
    }

    [Theory]
    [MemberData(nameof(AllTargetMachineStates))]
    public void Convert_ForegroundRole_ReturnsNonNullFrozenBrush(TargetMachineState state)
    {
        // Arrange & Act
        var result = _sut.Convert(state, typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        Assert.True(brush.IsFrozen);
        Assert.NotNull(brush);
    }

    [Fact]
    public void Convert_InvalidRole_DefaultsToBackgroundBrush()
    {
        // Arrange
        var state = TargetMachineState.CompletedSuccess;

        // Act
        var defaultResult = _sut.Convert(state, typeof(Brush), null, CultureInfo.InvariantCulture);
        var customResult = _sut.Convert(state, typeof(Brush), "UnknownRole", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(defaultResult, customResult);
    }

    [Fact]
    public void Convert_NonTargetMachineStateValue_DefaultsToPending()
    {
        // Arrange & Act
        var resultFromNull = _sut.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);
        var resultFromString = _sut.Convert("InvalidStateString", typeof(Brush), null, CultureInfo.InvariantCulture);
        var resultFromPending = _sut.Convert(TargetMachineState.Pending, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(resultFromPending, resultFromNull);
        Assert.Equal(resultFromPending, resultFromString);
    }

    [Fact]
    public void Convert_DifferentStates_ProduceDistinctColors()
    {
        // Arrange & Act
        var successBg = (SolidColorBrush)_sut.Convert(TargetMachineState.CompletedSuccess, typeof(Brush), null, CultureInfo.InvariantCulture);
        var errorBg = (SolidColorBrush)_sut.Convert(TargetMachineState.Error, typeof(Brush), null, CultureInfo.InvariantCulture);
        var warningBg = (SolidColorBrush)_sut.Convert(TargetMachineState.SkippedAlreadyExists, typeof(Brush), null, CultureInfo.InvariantCulture);
        var activeBg = (SolidColorBrush)_sut.Convert(TargetMachineState.Configuring, typeof(Brush), null, CultureInfo.InvariantCulture);
        var pendingBg = (SolidColorBrush)_sut.Convert(TargetMachineState.Pending, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.NotEqual(successBg.Color, errorBg.Color);
        Assert.NotEqual(successBg.Color, warningBg.Color);
        Assert.NotEqual(successBg.Color, activeBg.Color);
        Assert.NotEqual(successBg.Color, pendingBg.Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange & Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            _sut.ConvertBack(Brushes.Black, typeof(TargetMachineState), null, CultureInfo.InvariantCulture));
    }
}
