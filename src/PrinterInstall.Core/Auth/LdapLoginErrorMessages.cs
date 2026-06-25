using System.DirectoryServices.Protocols;

namespace PrinterInstall.Core.Auth;

public static class LdapLoginErrorMessages
{
    public const string DomainNameRequired = "O nome do domínio é obrigatório.";

    public const string AuthenticationFailed = "Falha ao autenticar no domínio.";

    public static string FromLdapException(LdapException ex) => FromLdapErrorCode(ex.ErrorCode);

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
}
