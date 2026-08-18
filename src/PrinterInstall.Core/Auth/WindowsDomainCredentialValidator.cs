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

        var (logonUserName, logonDomain) = ResolveLogonIdentity(domainName, credential);
        if (!LogonUser(
                logonUserName,
                logonDomain,
                credential.Password,
                Logon32LogonNetwork,
                Logon32ProviderDefault,
                out var token))
        {
            return Task.FromResult(LdapValidationResult.Failure(
                LdapLoginErrorMessages.FromWin32Error(Marshal.GetLastWin32Error())));
        }

        CloseHandle(token);
        return Task.FromResult(LdapValidationResult.Success());
    }

    /// <summary>
    /// LogonUser expects NetBIOS domain (PREVENTSENIOR) or UPN (user@domain.local with domain ".").
    /// DNS-only domain names fail with ERROR_LOGON_FAILURE (1326) even with valid credentials.
    /// </summary>
    internal static (string UserName, string Domain) ResolveLogonIdentity(
        string domainName,
        NetworkCredential credential)
    {
        var domain = domainName.Trim();
        var userName = credential.UserName;

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
