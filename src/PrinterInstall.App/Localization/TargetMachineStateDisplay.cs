using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Localization;

public static class TargetMachineStateDisplay
{
    public static string GetDisplay(TargetMachineState state) => state switch
    {
        TargetMachineState.Pending => "Pendente",
        TargetMachineState.ContactingRemote => "Conectando",
        TargetMachineState.ValidatingDriver => "Validando driver",
        TargetMachineState.InstallingDriver => "Instalando driver",
        TargetMachineState.DriverInstalledReconfirming => "Confirmando driver",
        TargetMachineState.Configuring => "Configurando",
        TargetMachineState.CompletedSuccess => "Concluído",
        TargetMachineState.SkippedAlreadyExists => "Já existe",
        TargetMachineState.AbortedDriverMissing => "Driver ausente",
        TargetMachineState.Error => "Erro",
        TargetMachineState.DeployCancelled => "Cancelado",
        TargetMachineState.RollbackRemovingQueue => "Removendo fila",
        TargetMachineState.RollbackRemovingPort => "Removendo porta",
        TargetMachineState.RolledBack => "Revertido",
        _ => state.ToString()
    };
}
