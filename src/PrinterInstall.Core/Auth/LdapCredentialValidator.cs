using System.DirectoryServices.Protocols;
using System.Net;

namespace PrinterInstall.Core.Auth;

public sealed class LdapCredentialValidator : ILdapCredentialValidator
{
    public Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return Task.FromResult(LdapValidationResult.Failure("Domain name is required."));

        try
        {
            var identifier = new LdapDirectoryIdentifier(domainName.Trim(), 389, false, false);
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
        catch (LdapException ex)
        {
            return Task.FromResult(LdapValidationResult.Failure($"LDAP error: {ex.Message} (0x{ex.ErrorCode:X})"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(LdapValidationResult.Failure(ex.Message));
        }
    }
}
