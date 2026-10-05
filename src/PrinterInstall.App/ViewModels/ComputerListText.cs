using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.App.ViewModels;

/// <summary>Operações sobre o texto "um computador por linha" usado nas telas de implantação e controle.</summary>
internal static class ComputerListText
{
    /// <summary>Acrescenta o computador local ao texto, a menos que ele já esteja na lista.</summary>
    public static string WithThisComputer(string current, LocalMachineIdentity identity)
    {
        if (ComputerNameListParser.Parse(current).Any(identity.IsLocalMachine))
            return current;

        var name = identity.GetPrimaryLocalName();
        return string.IsNullOrWhiteSpace(current)
            ? name
            : current.TrimEnd() + Environment.NewLine + name;
    }
}
