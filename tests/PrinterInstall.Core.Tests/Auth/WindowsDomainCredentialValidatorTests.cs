using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class WindowsDomainCredentialValidatorTests
{
    [Theory]
    [InlineData("laboratorio.test", "jsilva", "jsilva@laboratorio.test", null)]
    [InlineData("LABORATORIO", "jsilva", "jsilva", "LABORATORIO")]
    public void ResolveLogonIdentity_UsesUpnForDnsDomain(
        string domainName, string userName, string expectedUser, string? expectedDomain)
    {
        var credential = new NetworkCredential(userName, "secret", domainName);

        var (logonUser, logonDomain) = WindowsDomainCredentialValidator.ResolveLogonIdentity(domainName, credential);

        Assert.Equal(expectedUser, logonUser);
        Assert.Equal(expectedDomain, logonDomain);
    }

    [Theory]
    [InlineData("jsilva", "LABORATORIO", "jsilva", "LABORATORIO")]
    [InlineData("jsilva", "laboratorio.test", "jsilva@laboratorio.test", null)]
    [InlineData("jsilva@custom.example", "LABORATORIO", "jsilva@custom.example", null)]
    public void ResolveLogonIdentity_PreservesAccountDomainInsteadOfLdapHost(
        string user, string domain, string expectedUser, string? expectedDomain)
    {
        var result = WindowsDomainCredentialValidator.ResolveLogonIdentity(
            "dc01.laboratorio.test", new NetworkCredential(user, "secret", domain));
        Assert.Equal(expectedUser, result.UserName);
        Assert.Equal(expectedDomain, result.Domain);
    }
}
