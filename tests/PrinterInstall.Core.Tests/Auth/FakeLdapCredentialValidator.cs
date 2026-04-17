using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public sealed class FakeLdapCredentialValidator : ILdapCredentialValidator
{
    public bool NextResult { get; set; } = true;
    public string? NextError { get; set; }

    public Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(NextResult
            ? LdapValidationResult.Success()
            : LdapValidationResult.Failure(NextError ?? "fail"));
    }
}
