using System.Net;
using PrinterInstall.Core.Auth;
using Xunit;

namespace PrinterInstall.Core.Tests.Auth;

public sealed class CredentialHelperTests
{
    [Theory]
    [InlineData("000.000.000-00", "00000000000")]
    [InlineData("000.000.000/00", "00000000000")]
    [InlineData("00000000000", "00000000000")]
    [InlineData("  000.000.000-00  ", "00000000000")]
    [InlineData("usuario.adm", "usuario.adm")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeCpf_CleansProperly(string? input, string expected)
    {
        var result = CredentialHelper.SanitizeCpf(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("EMPRESA", "00000000000", @"EMPRESA\00000000000")]
    [InlineData("EMPRESA", @"EMPRESA\00000000000", @"EMPRESA\00000000000")]
    [InlineData("OUTRO", @"EMPRESA\00000000000", @"EMPRESA\00000000000")]
    [InlineData("EMPRESA", "user@empresa.test", "user@empresa.test")]
    [InlineData(null, "00000000000", "00000000000")]
    [InlineData("", "00000000000", "00000000000")]
    public void FormatDomainUser_FormatsCorrectlyWithoutDuplicates(string? domain, string? userName, string expected)
    {
        var result = CredentialHelper.FormatDomainUser(domain, userName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("jsilva", "laboratorio.test", "jsilva", "laboratorio.test")]
    [InlineData(@"LABORATORIO\jsilva", "laboratorio.test", "jsilva", "LABORATORIO")]
    [InlineData("jsilva@laboratorio.test", "laboratorio.test", "jsilva", "laboratorio.test")]
    [InlineData(@"DOMINIO\000.000.000-00", "padrao.test", "000.000.000-00", "DOMINIO")]
    [InlineData("", "padrao.test", "", "padrao.test")]
    [InlineData(null, "padrao.test", "", "padrao.test")]
    public void SplitDomainAndUser_SplitsCorrectly(string? input, string? defaultDomain, string expectedUser, string expectedDomain)
    {
        var (user, domain) = CredentialHelper.SplitDomainAndUser(input, defaultDomain);
        Assert.Equal(expectedUser, user);
        Assert.Equal(expectedDomain, domain);
    }

    [Fact]
    public void BuildCredentialUserName_WithDomainAndUser_ReturnsDownLevel()
    {
        var cred = new NetworkCredential("jsilva", "senha123", "LABORATORIO");
        var result = CredentialHelper.BuildCredentialUserName(cred);
        Assert.Equal(@"LABORATORIO\jsilva", result);
    }

    [Fact]
    public void BuildCredentialUserName_WhenUserAlreadyHasDomain_DoesNotDuplicate()
    {
        var cred = new NetworkCredential(@"LABORATORIO\jsilva", "senha123", "LABORATORIO");
        var result = CredentialHelper.BuildCredentialUserName(cred);
        Assert.Equal(@"LABORATORIO\jsilva", result);
    }
}
