using System.DirectoryServices.Protocols;
using System.Net.Sockets;

namespace PrinterInstall.Core.Auth;

public static class LdapLoginErrorMessages
{
    public const string DomainNameRequired = "O nome do domínio é obrigatório.";

    public const string AuthenticationFailed = "Falha ao autenticar no domínio.";

    public static string FromLdapException(LdapException ex) => FromLdapErrorCode(ex.ErrorCode);

    public static string FromException(Exception ex)
    {
        if (ex is LdapException ldap)
            return FromLdapException(ldap);

        if (ex is DirectoryOperationException { InnerException: LdapException innerLdap })
            return FromLdapException(innerLdap);

        return ex switch
        {
            SocketException => "Não foi possível contatar o servidor LDAP do domínio. Verifique rede, VPN e firewall (porta 389).",
            UnauthorizedAccessException => "Permissão insuficiente para autenticar no domínio.",
            System.Security.Authentication.AuthenticationException =>
                "Usuário ou senha inválidos.",
            _ => $"{AuthenticationFailed} ({ex.GetType().Name}: {ex.Message})"
        };
    }

    public static string FromLdapErrorCode(int errorCode) => errorCode switch
    {
        0x31 => "Usuário ou senha inválidos.",
        0x32 => "Permissão insuficiente para autenticar no domínio.",
        0x33 => "Autenticação não suportada.",
        0x34 => "Credencial inválida para este tipo de autenticação.",
        0x51 => "Não foi possível contatar o servidor LDAP do domínio.",
        0x52 => "Erro local ao conectar ao domínio.",
        0x53 => "Erro de codificação ao contatar o domínio.",
        0x54 => "Erro de decodificação ao contatar o domínio.",
        0x55 => "Tempo esgotado ao contatar o domínio.",
        0x71 => "Servidor LDAP do domínio indisponível.",
        _ => $"{AuthenticationFailed} (código 0x{errorCode:X})."
    };

    public static string FromWin32Error(int errorCode) => errorCode switch
    {
        1326 => "Usuário ou senha inválidos.",
        1327 => "Restrição de conta impediu o login.",
        1330 => "Nome de usuário inválido.",
        1331 => "Conta de usuário desabilitada.",
        1351 => "Não foi possível contatar o servidor do domínio.",
        1907 => "Conta de usuário expirada.",
        1909 => "A senha expirou e precisa ser alterada.",
        1722 or 53 => "Não foi possível contatar o servidor do domínio. Verifique rede e VPN.",
        _ => $"{AuthenticationFailed} (Win32 {errorCode})."
    };
}
