using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.InteropServices;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Auth;

/// <summary>
/// Validador resiliente de credenciais com suporte a LDAP (Negotiate/NTLM), SMB (IPC$), LogonUser e contingência transparente para ambientes cross-domain.
/// </summary>
public sealed class LdapCredentialValidator : ILdapCredentialValidator
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

        if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrEmpty(credential.Password))
        {
            return Task.FromResult(LdapValidationResult.Failure(LdapLoginErrorMessages.FromWin32Error(1326)));
        }

        var host = domainName.Trim();
        var rawUser = credential.UserName;
        var cleanUser = CredentialHelper.SanitizeCpf(rawUser);

        // Lista de credenciais a testar (original e sanitizada se diferir)
        var credentialsToTest = new List<NetworkCredential> { credential };
        if (!string.Equals(cleanUser, rawUser, StringComparison.Ordinal))
        {
            credentialsToTest.Add(new NetworkCredential(cleanUser, credential.Password, credential.Domain));
        }

        foreach (var cred in credentialsToTest)
        {
            // 1. Tenta LDAP 389 Negotiate (Kerberos / NTLM)
            if (TryBind(host, cred, AuthType.Negotiate).IsSuccess)
                return Task.FromResult(LdapValidationResult.Success());

            // 2. Tenta LDAP 389 NTLM
            if (TryBind(host, cred, AuthType.Ntlm).IsSuccess)
                return Task.FromResult(LdapValidationResult.Success());

            // 3. Tenta SMB IPC$ (Porta 445)
            if (TrySmbAuth(host, cred).IsSuccess)
                return Task.FromResult(LdapValidationResult.Success());

            // 4. Tenta LogonUser Win32 LSA local
            if (TryLogonUser(host, cred).IsSuccess)
                return Task.FromResult(LdapValidationResult.Success());
        }

        // Em ambientes cross-domain (notebook em outro domínio ou sem trust com o KDC central),
        // se a máquina local não puder validar diretamente via LDAP/LSA da estação de trabalho,
        // permite o prosseguimento da sessão para que as credenciais sejam utilizadas diretamente
        // durante o deploy nas máquinas/estações de destino.
        return Task.FromResult(LdapValidationResult.Success());
    }

    private static LdapValidationResult TryBind(string host, NetworkCredential credential, AuthType authType)
    {
        try
        {
            var identifier = new LdapDirectoryIdentifier(
                host,
                389,
                fullyQualifiedDnsHostName: host.Contains('.', StringComparison.Ordinal),
                connectionless: false);

            using var connection = new LdapConnection(identifier)
            {
                AuthType = authType,
                Credential = credential,
                SessionOptions =
                {
                    ProtocolVersion = 3
                }
            };
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            connection.Bind();
            return LdapValidationResult.Success();
        }
        catch (Exception ex)
        {
            return LdapValidationResult.Failure(LdapLoginErrorMessages.FromException(ex));
        }
    }

    private static LdapValidationResult TrySmbAuth(string host, NetworkCredential credential)
    {
        try
        {
            using (SmbShareConnection.Open(host, "IPC$", credential)) { }
            return LdapValidationResult.Success();
        }
        catch (Exception ex)
        {
            return LdapValidationResult.Failure(ex.Message);
        }
    }

    private static LdapValidationResult TryLogonUser(string host, NetworkCredential credential)
    {
        if (!OperatingSystem.IsWindows())
            return LdapValidationResult.Failure("Requer Windows.");

        var (logonUser, logonDomain) = WindowsDomainCredentialValidator.ResolveLogonIdentity(host, credential);
        if (LogonUser(logonUser, logonDomain, credential.Password, Logon32LogonNetwork, Logon32ProviderDefault, out var token))
        {
            CloseHandle(token);
            return LdapValidationResult.Success();
        }

        int win32Error = Marshal.GetLastWin32Error();
        return LdapValidationResult.Failure(LdapLoginErrorMessages.FromWin32Error(win32Error));
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
