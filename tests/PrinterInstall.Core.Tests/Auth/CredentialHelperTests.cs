using System.Net;
using PrinterInstall.Core.Auth;
using Xunit;

namespace PrinterInstall.Core.Tests.Auth;

public sealed class CredentialHelperTests
{
    [Theory]
    [InlineData("123.456.789-00", "12345678900")]
    [InlineData("123.456.789/00", "12345678900")]
    [InlineData("12345678900", "12345678900")]
    [InlineData("  123.456.789-00  ", "12345678900")]
    [InlineData("usuario.adm", "usuario.adm")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeCpf_CleansProperly(string? input, string expected)
    {
        var result = CredentialHelper.SanitizeCpf(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("EMPRESA", "12345678900", @"EMPRESA\12345678900")]
    [InlineData("EMPRESA", @"EMPRESA\12345678900", @"EMPRESA\12345678900")]
    [InlineData("OUTRO", @"EMPRESA\12345678900", @"EMPRESA\12345678900")]
    [InlineData("EMPRESA", "user@empresa.local", "user@empresa.local")]
    [InlineData(null, "12345678900", "12345678900")]
    [InlineData("", "12345678900", "12345678900")]
    public void FormatDomainUser_FormatsCorrectlyWithoutDuplicates(string? domain, string? userName, string expected)
    {
        var result = CredentialHelper.FormatDomainUser(domain, userName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("jsilva", "preventsenior.local", "jsilva", "preventsenior.local")]
    [InlineData(@"PREVENTSENIOR\jsilva", "preventsenior.local", "jsilva", "PREVENTSENIOR")]
    [InlineData("jsilva@preventsenior.local", "preventsenior.local", "jsilva", "preventsenior.local")]
    [InlineData(@"DOMINIO\123.456.789-00", "padrao.local", "123.456.789-00", "DOMINIO")]
    [InlineData("", "padrao.local", "", "padrao.local")]
    [InlineData(null, "padrao.local", "", "padrao.local")]
    public void SplitDomainAndUser_SplitsCorrectly(string? input, string? defaultDomain, string expectedUser, string expectedDomain)
    {
        var (user, domain) = CredentialHelper.SplitDomainAndUser(input, defaultDomain);
        Assert.Equal(expectedUser, user);
        Assert.Equal(expectedDomain, domain);
    }

    [Fact]
    public void BuildCredentialUserName_WithDomainAndUser_ReturnsDownLevel()
    {
        var cred = new NetworkCredential("jsilva", "senha123", "PREVENTSENIOR");
        var result = CredentialHelper.BuildCredentialUserName(cred);
        Assert.Equal(@"PREVENTSENIOR\jsilva", result);
    }

    [Fact]
    public void BuildCredentialUserName_WhenUserAlreadyHasDomain_DoesNotDuplicate()
    {
        var cred = new NetworkCredential(@"PREVENTSENIOR\jsilva", "senha123", "PREVENTSENIOR");
        var result = CredentialHelper.BuildCredentialUserName(cred);
        Assert.Equal(@"PREVENTSENIOR\jsilva", result);
    }
}
