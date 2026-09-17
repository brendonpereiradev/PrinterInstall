using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace PrinterInstall.Core.Auth;

public sealed class WindowsDomainCredentialValidator : ILdapCredentialValidator
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    public Task<LdapValidationResult> ValidateAsync(
        string domainName,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return Task.FromResult(LdapValidationResult.Failure(LdapLoginErrorMessages.DomainNameRequired));

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(LdapValidationResult.Failure(
                "Autenticação de domínio requer Windows."));
        }

        if (string.IsNullOrWhiteSpace(credential.UserName) || credential.Password is null)
        {
            return Task.FromResult(LdapValidationResult.Failure(
                LdapLoginErrorMessages.FromWin32Error(1326)));
        }

        // 1. Tenta autenticação direta com o usuário fornecido
        var (logonUserName, logonDomain) = ResolveLogonIdentity(domainName, credential);
        if (LogonUser(
                logonUserName,
                logonDomain,
                credential.Password,
                Logon32LogonNetwork,
                Logon32ProviderDefault,
                out var token))
        {
            CloseHandle(token);
            return Task.FromResult(LdapValidationResult.Success());
        }

        int win32Error = Marshal.GetLastWin32Error();

        // 2. Fallback: Se o usuário informou um CPF com formatação/pontuação (ex: 123.456.789-00), tenta com CPF sanitizado
        var cleanUser = CredentialHelper.SanitizeCpf(credential.UserName);
        if (!string.Equals(cleanUser, credential.UserName, StringComparison.Ordinal))
        {
            var sanitizedCred = new NetworkCredential(cleanUser, credential.Password, credential.Domain);
            var (sanitizedLogonUser, sanitizedLogonDomain) = ResolveLogonIdentity(domainName, sanitizedCred);
            if (LogonUser(
                    sanitizedLogonUser,
                    sanitizedLogonDomain,
                    sanitizedCred.Password,
                    Logon32LogonNetwork,
                    Logon32ProviderDefault,
                    out var sanitizedToken))
            {
                CloseHandle(sanitizedToken);
                return Task.FromResult(LdapValidationResult.Success());
            }

            win32Error = Marshal.GetLastWin32Error();
        }

        return Task.FromResult(LdapValidationResult.Failure(
            LdapLoginErrorMessages.FromWin32Error(win32Error)));
    }

    /// <summary>
    /// LogonUser espera NetBIOS domain (PREVENTSENIOR) ou UPN (user@domain.local com domain ".").
    /// Nomes de domínio somente DNS falham com ERROR_LOGON_FAILURE (1326) mesmo com credenciais válidas se passados no campo de domínio.
    /// </summary>
    internal static (string UserName, string Domain) ResolveLogonIdentity(
        string domainName,
        NetworkCredential credential)
    {
        var rawUser = credential.UserName ?? string.Empty;
        var (extractedUser, extractedDomain) = CredentialHelper.SplitDomainAndUser(rawUser, domainName);

        var domain = !string.IsNullOrWhiteSpace(extractedDomain) ? extractedDomain.Trim() : domainName.Trim();
        var userName = extractedUser.Trim();

        if (domain.Contains('.', StringComparison.Ordinal))
            return ($"{userName}@{domain}", ".");

        return (userName, domain);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    private static extern bool LogonUser(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
