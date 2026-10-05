using System.DirectoryServices.Protocols;
using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapCredentialValidatorTests
{
    [Theory]
    [InlineData("", "user", "password")]
    [InlineData("test.example", "", "password")]
    [InlineData("test.example", "user", "")]
    public async Task ValidateAsync_InvalidInput_DoesNotAttemptAuthentication(string domain, string user, string password)
    {
        var sut = new LdapCredentialValidator(
            (_, _, _) => throw new InvalidOperationException("LDAP must not be called."),
            (_, _) => throw new InvalidOperationException("SMB must not be called."),
            (_, _) => throw new InvalidOperationException("LogonUser must not be called."));

        var result = await sut.ValidateAsync(domain, new NetworkCredential(user, password));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ValidateAsync_AlreadyCanceled_DoesNotAttemptAuthentication()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = new LdapCredentialValidator(
            (_, _, _) => throw new InvalidOperationException("LDAP must not be called."),
            (_, _) => throw new InvalidOperationException("SMB must not be called."),
            (_, _) => throw new InvalidOperationException("LogonUser must not be called."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.ValidateAsync("test.example", new NetworkCredential("user", "password"), cts.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_CanceledDuringBind_StopsBeforeFallbackOrSuccess(bool success)
    {
        using var cts = new CancellationTokenSource();
        var bindCalls = 0;
        var sut = new LdapCredentialValidator(
            (_, _, _) =>
            {
                bindCalls++;
                cts.Cancel();
                return success ? LdapValidationResult.Success() : LdapValidationResult.Failure("Unavailable.");
            },
            (_, _) => throw new InvalidOperationException("SMB must not be called after cancellation."),
            (_, _) => throw new InvalidOperationException("LogonUser must not be called after cancellation."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.ValidateAsync("test.example", new NetworkCredential("user", "password"), cts.Token));
        Assert.Equal(1, bindCalls);
    }

    [Fact]
    public async Task ValidateAsync_FormattedCpf_RetriesSanitizedIdentityInOriginalOrder()
    {
        var attempts = new List<(string Method, string User)>();
        LdapValidationResult Attempt(string method, NetworkCredential credential)
        {
            attempts.Add((method, credential.UserName));
            Assert.Equal("test-domain", credential.Domain);
            return credential.UserName == "00000000000"
                ? LdapValidationResult.Success()
                : LdapValidationResult.Failure("Try sanitized identity.");
        }

        var sut = new LdapCredentialValidator(
            (_, credential, auth) => Attempt(auth.ToString(), credential),
            (_, credential) => Attempt("SMB", credential),
            (_, credential) => Attempt("LogonUser", credential));

        var result = await sut.ValidateAsync("test.example", new NetworkCredential("000.000.000-00", "password", "test-domain"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[]
        {
            ("Negotiate", "000.000.000-00"), ("Ntlm", "000.000.000-00"),
            ("SMB", "000.000.000-00"), ("LogonUser", "000.000.000-00"),
            ("Negotiate", "00000000000")
        }, attempts);
    }
}
