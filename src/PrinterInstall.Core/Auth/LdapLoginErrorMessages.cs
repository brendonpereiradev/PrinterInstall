using System.ComponentModel;
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
        0x07 or 0x08 or 0x30 => "Autenticação não suportada.",
        0x33 => "Servidor LDAP do domínio ocupado.",
        0x34 => "Servidor LDAP do domínio indisponível.",
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
        1311 => "Nenhum servidor de logon disponível (Win32 1311 / ERROR_NO_LOGON_SERVERS). Verifique o domínio da conta, DNS e comunicação com um controlador de domínio.",
        1355 => "O domínio informado não existe ou não pôde ser contatado (Win32 1355). Verifique domínio, DNS e rede.",
        1326 => "Usuário ou senha inválidos.",
        1327 => "Restrição de conta impediu o login.",
        1328 => "O horário de acesso desta conta é restrito.",
        1329 => "Esta conta não pode entrar neste computador.",
        1330 => "A senha expirou e precisa ser alterada.",
        1331 => "Conta de usuário desabilitada.",
        1351 => "Não foi possível contatar o servidor do domínio.",
        1793 => "Conta de usuário expirada.",
        1907 => "A senha precisa ser alterada antes de entrar.",
        1909 => "Conta de usuário bloqueada.",
        1722 or 53 => "Não foi possível contatar o servidor do domínio. Verifique rede e VPN.",
        _ => $"{AuthenticationFailed} (Win32 {errorCode})."
    };

    public static LoginFailureKind KindFromException(Exception ex) => ex switch
    {
        LdapException ldap => KindFromLdapErrorCode(ldap.ErrorCode),
        DirectoryOperationException { InnerException: LdapException ldap } => KindFromLdapErrorCode(ldap.ErrorCode),
        DirectoryOperationException { Response: { } response } => KindFromLdapErrorCode((int)response.ResultCode),
        SocketException { SocketErrorCode: SocketError.TimedOut } => LoginFailureKind.Timeout,
        SocketException => LoginFailureKind.NetworkUnavailable,
        Win32Exception win32 => KindFromWin32Error(win32.NativeErrorCode),
        TimeoutException => LoginFailureKind.Timeout,
        UnauthorizedAccessException => LoginFailureKind.AccessDenied,
        System.Security.Authentication.AuthenticationException => LoginFailureKind.InvalidCredentials,
        _ => LoginFailureKind.Unknown
    };

    public static LoginFailureKind KindFromLdapErrorCode(int errorCode) => errorCode switch
    {
        0x31 => LoginFailureKind.InvalidCredentials,
        0x32 => LoginFailureKind.AccessDenied,
        0x07 or 0x08 or 0x30 => LoginFailureKind.UnsupportedAuthentication,
        0x33 or 0x34 or 0x51 or 0x71 => LoginFailureKind.NetworkUnavailable,
        0x03 or 0x55 => LoginFailureKind.Timeout,
        _ => LoginFailureKind.Unknown
    };

    public static LoginFailureKind KindFromWin32Error(int errorCode) => errorCode switch
    {
        1326 => LoginFailureKind.InvalidCredentials,
        1327 or 1328 or 1329 => LoginFailureKind.AccountRestricted,
        1330 => LoginFailureKind.PasswordExpired,
        1331 => LoginFailureKind.AccountDisabled,
        1355 => LoginFailureKind.DomainNotFound,
        1793 => LoginFailureKind.AccountExpired,
        1907 => LoginFailureKind.PasswordMustChange,
        1909 => LoginFailureKind.AccountLocked,
        5 => LoginFailureKind.AccessDenied,
        121 or 1460 => LoginFailureKind.Timeout,
        53 or 64 or 67 or 1231 or 1232 or 1311 or 1351 or 1722 or 1908 => LoginFailureKind.NetworkUnavailable,
        _ => LoginFailureKind.Unknown
    };

    // Uma resposta específica sobre a conta é mais útil que falhas de conectividade
    // nos outros métodos. Uma falha de rede, sozinha, nunca implica senha incorreta.
    internal static LoginFailureKind SelectFailureKind(IEnumerable<LdapValidationResult> failures) =>
        failures.Select(f => f.FailureKind).OrderByDescending(Priority).FirstOrDefault();

    private static int Priority(LoginFailureKind kind) => kind switch
    {
        LoginFailureKind.AccountLocked => 100,
        LoginFailureKind.AccountDisabled or LoginFailureKind.AccountExpired => 90,
        LoginFailureKind.PasswordExpired or LoginFailureKind.PasswordMustChange => 80,
        LoginFailureKind.AccountRestricted => 70,
        LoginFailureKind.InvalidCredentials => 60,
        LoginFailureKind.AccessDenied => 50,
        LoginFailureKind.DomainNotFound => 40,
        LoginFailureKind.Timeout => 30,
        LoginFailureKind.NetworkUnavailable => 20,
        LoginFailureKind.UnsupportedAuthentication => 10,
        _ => 0
    };
}
