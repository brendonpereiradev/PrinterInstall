using System.Globalization;
using PrinterInstall.App.Converters;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Tests.Converters;

/// <summary>
/// Testes unitários para <see cref="TargetMachineStateToIconConverter"/>.
/// </summary>
public class TargetMachineStateToIconConverterTests
{
    private readonly TargetMachineStateToIconConverter _sut = new();

    [Theory]
    [InlineData(TargetMachineState.CompletedSuccess, "\uE73E")]
    [InlineData(TargetMachineState.SkippedAlreadyExists, "\uE946")]
    [InlineData(TargetMachineState.AbortedDriverMissing, "\uE711")]
    [InlineData(TargetMachineState.Error, "\uEA39")]
    [InlineData(TargetMachineState.ContactingRemote, "\uE895")]
    [InlineData(TargetMachineState.ValidatingDriver, "\uE9D9")]
    [InlineData(TargetMachineState.InstallingDriver, "\uE898")]
    [InlineData(TargetMachineState.DriverInstalledReconfirming, "\uE895")]
    [InlineData(TargetMachineState.Configuring, "\uE9F5")]
    [InlineData(TargetMachineState.DeployCancelled, "\uE71A")]
    [InlineData(TargetMachineState.RollbackRemovingQueue, "\uE7A7")]
    [InlineData(TargetMachineState.RollbackRemovingPort, "\uE704")]
    [InlineData(TargetMachineState.RolledBack, "\uEC61")]
    [InlineData(TargetMachineState.Pending, "\uE823")]
    public void Convert_GivenState_ReturnsExpectedUnicodeIcon(TargetMachineState state, string expectedIcon)
    {
        // Arrange & Act
        var result = _sut.Convert(state, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expectedIcon, result);
    }

    [Fact]
    public void Convert_NonStateValue_ReturnsPendingDefaultIcon()
    {
        // Arrange & Act
        var resultNull = _sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        var resultInvalid = _sut.Convert("NotAState", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("\uE823", resultNull);
        Assert.Equal("\uE823", resultInvalid);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange & Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            _sut.ConvertBack("\uE73E", typeof(TargetMachineState), null, CultureInfo.InvariantCulture));
    }
}
