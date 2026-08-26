using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Remote;
using Xunit;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardViewModelAddThisComputerTests
{
    private static RemovalWizardViewModel CreateSut(LocalMachineIdentity identity)
    {
        // AddThisComputer não usa sessão, operações remotas ou orquestrador.
        return new RemovalWizardViewModel(
            new SessionContext(),
            null!,
            null!,
            null,
            identity);
    }

    [Fact]
    public void AddThisComputer_EmptyList_AppendsMachineName()
    {
        // Arrange
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);

        // Act
        sut.AddThisComputerCommand.Execute(null);

        // Assert
        Assert.Equal(identity.GetPrimaryLocalName(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_ExistingRemote_AppendsOnNewLine()
    {
        // Arrange
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "pc-remoto-01";

        // Act
        sut.AddThisComputerCommand.Execute(null);

        // Assert
        Assert.Equal($"pc-remoto-01{Environment.NewLine}{identity.GetPrimaryLocalName()}", sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocal_DoesNotDuplicate()
    {
        // Arrange
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        var local = identity.GetPrimaryLocalName();
        sut.ComputersText = local.ToUpperInvariant();

        // Act
        sut.AddThisComputerCommand.Execute(null);

        // Assert
        Assert.Equal(local.ToUpperInvariant(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocalhostLiteral_DoesNotAppend()
    {
        // Arrange
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "localhost";

        // Act
        sut.AddThisComputerCommand.Execute(null);

        // Assert
        Assert.Equal("localhost", sut.ComputersText);
    }
}
