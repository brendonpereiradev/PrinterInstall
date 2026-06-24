using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelAddThisComputerTests
{
    private static MainViewModel CreateSut(LocalMachineIdentity identity)
    {
        // AddThisComputer não usa orquestrador nem service provider.
        return new MainViewModel(
            new SessionContext(),
            null!,
            null!,
            null!,
            identity);
    }

    [Fact]
    public void AddThisComputer_EmptyList_AppendsMachineName()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal(identity.GetPrimaryLocalName(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_ExistingRemote_AppendsOnNewLine()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "pc-remoto-01";

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal($"pc-remoto-01{Environment.NewLine}{identity.GetPrimaryLocalName()}", sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocal_DoesNotDuplicate()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        var local = identity.GetPrimaryLocalName();
        sut.ComputersText = local.ToUpperInvariant();

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal(local.ToUpperInvariant(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocalhostLiteral_DoesNotAppend()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "localhost";

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal("localhost", sut.ComputersText);
    }
}
