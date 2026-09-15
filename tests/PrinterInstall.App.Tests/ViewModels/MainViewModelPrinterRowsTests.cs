using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelPrinterRowsTests
{
    private readonly Mock<ISessionContext> _sessionMock = new();
    private readonly Mock<IRemotePrinterOperations> _remoteOpsMock = new();

    private MainViewModel CreateSut()
    {
        var orchestrator = new PrinterDeploymentOrchestrator(_remoteOpsMock.Object);
        var controlOrchestrator = new PrinterControlOrchestrator(_remoteOpsMock.Object);
        var rollbackRunner = new DeploymentRollbackRunner(_remoteOpsMock.Object, controlOrchestrator);
        var localMachineIdentity = new LocalMachineIdentity();

        return new MainViewModel(
            _sessionMock.Object,
            orchestrator,
            rollbackRunner,
            null!,
            localMachineIdentity);
    }

    [Fact]
    public void Constructor_InitializesWithSingleRow_CanRemovePrinterRowIsFalse()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        Assert.Single(sut.PrinterRows);
        Assert.False(sut.CanRemovePrinterRow);
        Assert.False(sut.RemovePrinterRowCommand.CanExecute(null));
    }

    [Fact]
    public void AddPrinterRowCommand_IncreasesRowCount_CanRemovePrinterRowIsTrue()
    {
        // Arrange
        var sut = CreateSut();
        var propertyChangedFired = false;
        sut.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(sut.CanRemovePrinterRow))
            {
                propertyChangedFired = true;
            }
        };

        // Act
        sut.AddPrinterRowCommand.Execute(null);

        // Assert
        Assert.Equal(2, sut.PrinterRows.Count);
        Assert.True(sut.CanRemovePrinterRow);
        Assert.True(sut.RemovePrinterRowCommand.CanExecute(null));
        Assert.True(propertyChangedFired);
    }

    [Fact]
    public void RemovePrinterRowCommand_WhenMultipleRows_RemovesLastRowAndUpdatesCanRemove()
    {
        // Arrange
        var sut = CreateSut();
        sut.AddPrinterRowCommand.Execute(null);
        Assert.Equal(2, sut.PrinterRows.Count);
        Assert.True(sut.CanRemovePrinterRow);

        // Act
        sut.RemovePrinterRowCommand.Execute(null);

        // Assert
        Assert.Single(sut.PrinterRows);
        Assert.False(sut.CanRemovePrinterRow);
        Assert.False(sut.RemovePrinterRowCommand.CanExecute(null));
    }

    [Fact]
    public void RemovePrinterRowCommand_WhenSingleRow_DoesNotRemoveRow()
    {
        // Arrange
        var sut = CreateSut();
        Assert.Single(sut.PrinterRows);

        // Act
        sut.RemovePrinterRowCommand.Execute(null);

        // Assert
        Assert.Single(sut.PrinterRows);
        Assert.False(sut.CanRemovePrinterRow);
    }

    [Fact]
    public void RemovePrinterRowCommand_WithSpecificRow_RemovesSpecifiedRow()
    {
        // Arrange
        var sut = CreateSut();
        sut.AddPrinterRowCommand.Execute(null);
        var firstRow = sut.PrinterRows[0];
        firstRow.DisplayName = "Primeira";
        var secondRow = sut.PrinterRows[1];
        secondRow.DisplayName = "Segunda";

        // Act
        sut.RemovePrinterRowCommand.Execute(firstRow);

        // Assert
        Assert.Single(sut.PrinterRows);
        Assert.Same(secondRow, sut.PrinterRows[0]);
        Assert.False(sut.CanRemovePrinterRow);
    }
}
