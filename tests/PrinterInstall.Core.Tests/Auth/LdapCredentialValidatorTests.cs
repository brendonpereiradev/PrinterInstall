using System.DirectoryServices.Protocols;
using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapCredentialValidatorTests
{
    [Theory]
    [InlineData(LoginFailureKind.AccountLocked)]
    [InlineData(LoginFailureKind.AccountDisabled)]
    [InlineData(LoginFailureKind.PasswordExpired)]
    [InlineData(LoginFailureKind.PasswordMustChange)]
    [InlineData(LoginFailureKind.AccountExpired)]
    public async Task ValidateAsync_SpecificAccountFailure_IsPreservedWhenOtherMethodsFail(LoginFailureKind kind)
    {
        var sut = new LdapCredentialValidator(
            (_, _, _) => LdapValidationResult.Failure("LDAP rejected credentials", LoginFailureKind.InvalidCredentials),
            (_, _) => LdapValidationResult.Failure("SMB account status", kind),
            (_, _) => LdapValidationResult.Failure("No local logon server", LoginFailureKind.NetworkUnavailable));

        var result = await sut.ValidateAsync("test.example", new NetworkCredential("user", "password", "TEST"));

        Assert.False(result.IsSuccess);
        Assert.Equal(kind, result.FailureKind);
        Assert.Contains("LDAP rejected credentials", result.ErrorMessage);
        Assert.Contains("SMB account status", result.ErrorMessage);
        Assert.Contains("No local logon server", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_OnlyNetworkFailures_DoesNotReportInvalidCredentials()
    {
        LdapValidationResult Fail() => LdapValidationResult.Failure("Network unavailable", LoginFailureKind.NetworkUnavailable);
        var sut = new LdapCredentialValidator((_, _, _) => Fail(), (_, _) => Fail(), (_, _) => Fail());

        var result = await sut.ValidateAsync("test.example", new NetworkCredential("user", "password"));

        Assert.Equal(LoginFailureKind.NetworkUnavailable, result.FailureKind);
        Assert.Equal(1, result.ErrorMessage!.Split("Network unavailable").Length - 1);
    }

    [Theory]
    [InlineData("Usuário ou senha inválidos.")]
    [InlineData("Nenhum servidor de logon disponível (Win32 1311).")]
    public async Task ValidateAsync_AllMethodsFail_NeverCreatesSuccessfulLogin(string detail)
    {
        var count = 0;
        LdapValidationResult Fail()
        {
            count++;
            return LdapValidationResult.Failure(detail);
        }
        var sut = new LdapCredentialValidator((_, _, _) => Fail(), (_, _) => Fail(), (_, _) => Fail());
        var result = await sut.ValidateAsync("laboratorio.test", new NetworkCredential("user", "secret", "LABORATORIO"));
        Assert.False(result.IsSuccess);
        Assert.Null(result.ValidatedUserName);
        Assert.Contains(detail, result.ErrorMessage);
        Assert.DoesNotContain("secret", result.ErrorMessage);
        Assert.Equal(4, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ValidateAsync_ARealSuccessInAnyMethod_ReturnsValidatedIdentity(int successfulAttempt)
    {
        var count = 0;
        LdapValidationResult Attempt() => count++ == successfulAttempt
            ? LdapValidationResult.Success() : LdapValidationResult.Failure("unavailable");
        var sut = new LdapCredentialValidator((_, _, _) => Attempt(), (_, _) => Attempt(), (_, _) => Attempt());
        var result = await sut.ValidateAsync("laboratorio.test", new NetworkCredential("user", "secret", "LABORATORIO"));
        Assert.True(result.IsSuccess);
        Assert.Equal("user", result.ValidatedUserName);
        Assert.Equal("LABORATORIO", result.ValidatedDomain);
        Assert.Equal(successfulAttempt + 1, count);
    }

    [Theory]
    [InlineData("user", "laboratorio.test", "user@laboratorio.test", "")]
    [InlineData("user@custom.example", "LABORATORIO", "user@custom.example", "")]
    [InlineData("user", "LABORATORIO", "user", "LABORATORIO")]
    public void BuildBindCredential_UsesUpnOrNetbiosWithoutCombiningBoth(
        string user, string domain, string expectedUser, string expectedDomain)
    {
        var original = new NetworkCredential(user, "secret", domain);
        var result = LdapCredentialValidator.BuildBindCredential(original);
        Assert.Equal(expectedUser, result.UserName);
        Assert.Equal(expectedDomain, result.Domain);
        Assert.Equal("secret", result.Password);
        Assert.Equal(user, original.UserName);
    }

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
        Assert.Equal("00000000000", result.ValidatedUserName);
        Assert.Equal(new[]
        {
            ("Negotiate", "000.000.000-00"), ("Ntlm", "000.000.000-00"),
            ("SMB", "000.000.000-00"), ("LogonUser", "000.000.000-00"),
            ("Negotiate", "00000000000")
        }, attempts);
    }
}
