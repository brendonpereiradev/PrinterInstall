using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Localization;

public static class TargetMachineStateDisplay
{
    public static string GetDisplay(TargetMachineState state) => state switch
    {
        TargetMachineState.Pending => "Pendente",
        TargetMachineState.ContactingRemote => "Contactando o host remoto",
        TargetMachineState.ValidatingDriver => "Validando driver",
        TargetMachineState.InstallingDriver => "Instalando driver",
        TargetMachineState.DriverInstalledReconfirming => "Reconfirmando driver",
        TargetMachineState.Configuring => "Configurando impressora",
        TargetMachineState.CompletedSuccess => "Concluído com sucesso",
        TargetMachineState.SkippedAlreadyExists => "Ignorado (já existia)",
        TargetMachineState.AbortedDriverMissing => "Cancelado — driver ausente",
        TargetMachineState.Error => "Erro",
        _ => state.ToString()
    };
}
