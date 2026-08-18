using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelCredentialIdentityTests
{
    [Theory]
    [InlineData("jsilva", "preventsenior.local", "jsilva", "preventsenior.local")]
    [InlineData("PREVENTSENIOR\\jsilva", "preventsenior.local", "jsilva", "PREVENTSENIOR")]
    [InlineData("jsilva@preventsenior.local", "preventsenior.local", "jsilva", "preventsenior.local")]
    public void ParseCredentialIdentity_NormalizesUserInput(
        string rawUser, string configuredDomain, string expectedUser, string expectedDomain)
    {
        var (userName, domainName) = LoginViewModel.ParseCredentialIdentity(rawUser, configuredDomain);

        Assert.Equal(expectedUser, userName);
        Assert.Equal(expectedDomain, domainName);
    }

    [Theory]
    [InlineData("preventsenior.local", "preventsenior.local", "preventsenior.local")]
    [InlineData("PREVENTSENIOR", "preventsenior.local", "preventsenior.local")]
    public void ResolveLdapHost_UsesDnsDomainForNetBiOS(
        string parsedDomain, string configuredDomain, string expectedHost)
    {
        Assert.Equal(expectedHost, LoginViewModel.ResolveLdapHost(parsedDomain, configuredDomain));
    }
}
