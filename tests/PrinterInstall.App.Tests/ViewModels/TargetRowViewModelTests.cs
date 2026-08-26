using PrinterInstall.App.Localization;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Tests.ViewModels;

/// <summary>
/// Testes unitários para <see cref="TargetRowViewModel"/>.
/// </summary>
public class TargetRowViewModelTests
{
    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        // Arrange & Act
        var sut = new TargetRowViewModel();

        // Assert
        Assert.Equal(string.Empty, sut.ComputerName);
        Assert.False(sut.IsLocalMachine);
        Assert.Equal(string.Empty, sut.ComputerNameDisplay);
        Assert.Equal(string.Empty, sut.PrinterQueueName);
        Assert.Equal(string.Empty, sut.ExpectedPortName);
        Assert.Equal(TargetMachineState.Pending, sut.State);
        Assert.Equal(TargetMachineStateDisplay.GetDisplay(TargetMachineState.Pending), sut.StateDisplay);
        Assert.Equal(string.Empty, sut.Message);
    }

    [Fact]
    public void PropertySetters_UpdateValuesCorrectly()
    {
        // Arrange
        var sut = new TargetRowViewModel();

        // Act
        sut.ComputerName = "PC-LAB-01";
        sut.IsLocalMachine = true;
        sut.ComputerNameDisplay = "PC-LAB-01 (Este computador)";
        sut.PrinterQueueName = "EPSON_TM_T20X";
        sut.ExpectedPortName = "10.1.152.88";
        sut.Message = "Instalação finalizada.";

        // Assert
        Assert.Equal("PC-LAB-01", sut.ComputerName);
        Assert.True(sut.IsLocalMachine);
        Assert.Equal("PC-LAB-01 (Este computador)", sut.ComputerNameDisplay);
        Assert.Equal("EPSON_TM_T20X", sut.PrinterQueueName);
        Assert.Equal("10.1.152.88", sut.ExpectedPortName);
        Assert.Equal("Instalação finalizada.", sut.Message);
    }

    [Theory]
    [InlineData(TargetMachineState.ContactingRemote)]
    [InlineData(TargetMachineState.ValidatingDriver)]
    [InlineData(TargetMachineState.InstallingDriver)]
    [InlineData(TargetMachineState.Configuring)]
    [InlineData(TargetMachineState.CompletedSuccess)]
    [InlineData(TargetMachineState.Error)]
    [InlineData(TargetMachineState.SkippedAlreadyExists)]
    [InlineData(TargetMachineState.AbortedDriverMissing)]
    [InlineData(TargetMachineState.DeployCancelled)]
    [InlineData(TargetMachineState.RolledBack)]
    public void SettingState_AutomaticallyUpdatesStateDisplay(TargetMachineState newState)
    {
        // Arrange
        var sut = new TargetRowViewModel();

        // Act
        sut.State = newState;

        // Assert
        Assert.Equal(newState, sut.State);
        Assert.Equal(TargetMachineStateDisplay.GetDisplay(newState), sut.StateDisplay);
    }
}
