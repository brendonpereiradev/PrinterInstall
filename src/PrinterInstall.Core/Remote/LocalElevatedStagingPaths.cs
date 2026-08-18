using System.Security.AccessControl;
using System.Security.Principal;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Pasta temporária local para scripts com relançamento UAC (mesmo padrão do install.ps1 em %TEMP%).
/// </summary>
public sealed record LocalElevatedStagingPaths(string StagingId, string Root)
{
    public static LocalElevatedStagingPaths Create()
    {
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "PrinterInstall", id);
        Directory.CreateDirectory(root);
        GrantElevatedWriteAccess(root);
        return new LocalElevatedStagingPaths(id, root);
    }

    public string FilePath(string fileName) => Path.Combine(Root, fileName);

    /// <summary>
    /// Arquivos criados sem elevação precisam ser graváveis pelo processo relançado via RunAs (token elevado).
    /// </summary>
    internal static void GrantElevatedWriteAccess(string root)
    {
        try
        {
            var dirInfo = new DirectoryInfo(root);
            var security = dirInfo.GetAccessControl();

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            dirInfo.SetAccessControl(security);
        }
        catch
        {
            // Best effort — %TEMP% do usuário normalmente já permite RunAs no mesmo perfil.
        }
    }
}
