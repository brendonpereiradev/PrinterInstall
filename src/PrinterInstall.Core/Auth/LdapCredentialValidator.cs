using System.DirectoryServices.Protocols;
using System.Net;

namespace PrinterInstall.Core.Auth;

public sealed class LdapCredentialValidator : ILdapCredentialValidator
{
    public Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return Task.FromResult(LdapValidationResult.Failure(LdapLoginErrorMessages.DomainNameRequired));

        try
        {
            var host = domainName.Trim();
            var identifier = new LdapDirectoryIdentifier(
                host,
                389,
                fullyQualifiedDnsHostName: host.Contains('.', StringComparison.Ordinal),
                connectionless: false);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
                Credential = credential,
                SessionOptions =
                {
                    ProtocolVersion = 3
                }
            };
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            connection.Bind();
            return Task.FromResult(LdapValidationResult.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(LdapValidationResult.Failure(LdapLoginErrorMessages.FromException(ex)));
        }
    }
}
