using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelCredentialIdentityTests
{
    [Theory]
    [InlineData("jsilva", "laboratorio.test", "jsilva", "laboratorio.test")]
    [InlineData("LABORATORIO\\jsilva", "laboratorio.test", "jsilva", "LABORATORIO")]
    [InlineData("jsilva@laboratorio.test", "laboratorio.test", "jsilva", "laboratorio.test")]
    public void ParseCredentialIdentity_NormalizesUserInput(
        string rawUser, string configuredDomain, string expectedUser, string expectedDomain)
    {
        var (userName, domainName) = LoginViewModel.ParseCredentialIdentity(rawUser, configuredDomain);

        Assert.Equal(expectedUser, userName);
        Assert.Equal(expectedDomain, domainName);
    }

    [Theory]
    [InlineData("laboratorio.test", "laboratorio.test", "laboratorio.test")]
    [InlineData("LABORATORIO", "laboratorio.test", "laboratorio.test")]
    public void ResolveLdapHost_UsesDnsDomainForNetBiOS(
        string parsedDomain, string configuredDomain, string expectedHost)
    {
        Assert.Equal(expectedHost, LoginViewModel.ResolveLdapHost(parsedDomain, configuredDomain));
    }
}
