using System.ComponentModel;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Remote;

internal static class RemoteAuthenticationDiagnostics
{
    internal static string GetGuidance(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception { NativeErrorCode: 1311 or 1355 } native)
                return LdapLoginErrorMessages.FromWin32Error(native.NativeErrorCode) +
                    " Verifique essa comunicação tanto na origem quanto no destino. " +
                    "Se o destino foi informado por IP, tente seu nome DNS completo para permitir Kerberos. " +
                    "Esse código não comprova falta de privilégio administrativo; não depende de ter um perfil salvo.";
            if (current is Win32Exception { NativeErrorCode: 1326 })
                return "O Windows recusou o usuário ou a senha (Win32 1326). Confira a conta no formato DOMINIO\\usuario ou usuario@dominio.";
            if (current is Win32Exception { NativeErrorCode: 1219 })
                return "Já existe uma conexão SMB com outra conta (Win32 1219). Feche as conexões ao destino e tente novamente com a conta desejada.";
        }

        if (AccessDeniedDetector.IsAccessDenied(exception))
            return "Verifique se a conta informada pertence aos Administradores do destino e se as políticas permitem acesso remoto. " +
                "Ter um perfil salvo no notebook não é requisito; a elevação no computador de origem não concede permissões no destino.";

        return "Verifique os detalhes da falha, a comunicação SMB/RPC e as políticas de acesso remoto do destino.";
    }
}
