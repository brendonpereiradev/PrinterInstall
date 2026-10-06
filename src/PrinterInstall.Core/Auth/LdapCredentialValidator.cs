using System;
using System.Collections.Generic;
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

    private readonly Func<string, NetworkCredential, AuthType, LdapValidationResult> _bind;
    private readonly Func<string, NetworkCredential, LdapValidationResult> _smbAuth;
    private readonly Func<string, NetworkCredential, LdapValidationResult> _logonUser;

    public LdapCredentialValidator()
        : this(TryBind, TrySmbAuth, TryLogonUser)
    {
    }

    internal LdapCredentialValidator(
        Func<string, NetworkCredential, AuthType, LdapValidationResult> bind,
        Func<string, NetworkCredential, LdapValidationResult> smbAuth,
        Func<string, NetworkCredential, LdapValidationResult> logonUser)
    {
        _bind = bind;
        _smbAuth = smbAuth;
        _logonUser = logonUser;
    }

    public Task<LdapValidationResult> ValidateAsync(
        string domainName,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<LdapValidationResult>(cancellationToken);

        if (string.IsNullOrWhiteSpace(domainName))
            return Task.FromResult(LdapValidationResult.Failure(LdapLoginErrorMessages.DomainNameRequired));

        if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrEmpty(credential.Password))
        {
            return Task.FromResult(LdapValidationResult.Failure(
                LdapLoginErrorMessages.FromWin32Error(1326), LoginFailureKind.InvalidCredentials));
        }

        var host = domainName.Trim();
        var snapshot = new NetworkCredential(credential.UserName, credential.Password, credential.Domain);

        // Bind, SMB e LogonUser são chamadas bloqueantes. Task.FromResult após executá-las
        // não libera o Dispatcher; toda a sequência deve rodar fora da thread chamadora.
        return Task.Run(() => Validate(host, snapshot, cancellationToken), cancellationToken);
    }

    private LdapValidationResult Validate(string host, NetworkCredential credential, CancellationToken cancellationToken)
    {
        var failures = new List<LdapValidationResult>();
        bool Succeeded(Func<LdapValidationResult> attempt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = attempt();
            // As APIs nativas não aceitam CancellationToken. Verifica também após cada
            // chamada para não iniciar contingências nem concluir uma sessão cancelada.
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
                failures.Add(result);
            return result.IsSuccess;
        }

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
            if (Succeeded(() => _bind(host, cred, AuthType.Negotiate)))
                return LdapValidationResult.Success(cred);

            // 2. Tenta LDAP 389 NTLM
            if (Succeeded(() => _bind(host, cred, AuthType.Ntlm)))
                return LdapValidationResult.Success(cred);

            // 3. Tenta SMB IPC$ (Porta 445)
            if (Succeeded(() => _smbAuth(host, cred)))
                return LdapValidationResult.Success(cred);

            // 4. Tenta LogonUser Win32 LSA local
            if (Succeeded(() => _logonUser(host, cred)))
                return LdapValidationResult.Success(cred);
        }

        // Não apresentar uma autenticação recusada ou indisponível como login válido.
        var details = string.Join(Environment.NewLine, failures
            .Select(f => f.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal));
        return LdapValidationResult.Failure(
            "Não foi possível validar as credenciais no domínio." + Environment.NewLine + details,
            LdapLoginErrorMessages.SelectFailureKind(failures));
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
                Credential = BuildBindCredential(credential),
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
            return LdapValidationResult.Failure(
                $"LDAP ({authType}): {LdapLoginErrorMessages.FromException(ex)}{Environment.NewLine}{ex}",
                LdapLoginErrorMessages.KindFromException(ex));
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
            return LdapValidationResult.Failure(ex.Message, LdapLoginErrorMessages.KindFromException(ex));
        }
    }

    internal static NetworkCredential BuildBindCredential(NetworkCredential credential)
    {
        var identity = CredentialHelper.BuildCredentialUserName(credential);
        if (identity.Contains('@'))
            return new NetworkCredential(identity, credential.Password);
        var (user, domain) = CredentialHelper.SplitDomainAndUser(identity);
        return new NetworkCredential(user, credential.Password, domain);
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
        return LdapValidationResult.Failure(
            $"LogonUser (Win32 {win32Error}): {LdapLoginErrorMessages.FromWin32Error(win32Error)}",
            LdapLoginErrorMessages.KindFromWin32Error(win32Error));
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    private static extern bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
