using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class WindowsDomainCredentialValidatorTests
{
    [Theory]
    [InlineData("preventsenior.local", "jsilva", "jsilva@preventsenior.local", ".")]
    [InlineData("PREVENTSENIOR", "jsilva", "jsilva", "PREVENTSENIOR")]
    public void ResolveLogonIdentity_UsesUpnForDnsDomain(
        string domainName, string userName, string expectedUser, string expectedDomain)
    {
        var credential = new NetworkCredential(userName, "secret", domainName);

        var (logonUser, logonDomain) = WindowsDomainCredentialValidator.ResolveLogonIdentity(domainName, credential);

        Assert.Equal(expectedUser, logonUser);
        Assert.Equal(expectedDomain, logonDomain);
    }
}
