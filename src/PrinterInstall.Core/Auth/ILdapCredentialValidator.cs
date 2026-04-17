using System.Net;

namespace PrinterInstall.Core.Auth;

public interface ILdapCredentialValidator
{
    Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default);
}
