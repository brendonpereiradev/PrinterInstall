using PrinterInstall.App.Localization;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Tests.Localization;

public class TargetMachineStateDisplayTests
{
    [Fact]
    public void CompletedSuccess_returns_Portuguese_label()
    {
        var label = TargetMachineStateDisplay.GetDisplay(TargetMachineState.CompletedSuccess);
        Assert.Equal("Concluído com sucesso", label);
    }

    [Fact]
    public void Error_returns_Portuguese_label()
    {
        Assert.Equal("Erro", TargetMachineStateDisplay.GetDisplay(TargetMachineState.Error));
    }

    [Fact]
    public void Pending_returns_Portuguese_label()
    {
        Assert.Equal("Pendente", TargetMachineStateDisplay.GetDisplay(TargetMachineState.Pending));
    }

    [Fact]
    public void SkippedAlreadyExists_returns_Portuguese_label()
    {
        Assert.Equal("Ignorado (já existia)", TargetMachineStateDisplay.GetDisplay(TargetMachineState.SkippedAlreadyExists));
    }
}
