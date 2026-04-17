using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapCredentialValidatorContractTests
{
    [Fact]
    public async Task Fake_ReturnsSuccess()
    {
        var fake = new FakeLdapCredentialValidator { NextResult = true };
        var r = await fake.ValidateAsync("preventsenior.local", new NetworkCredential("u", "p"));
        Assert.True(r.IsSuccess);
    }
}
