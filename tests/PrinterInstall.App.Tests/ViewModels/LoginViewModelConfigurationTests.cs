using System.Net;
using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelConfigurationTests
{
    [Theory]
    [InlineData("usuario.teste")]
    [InlineData("LABORATORIO\\usuario.teste")]
    public async Task UnconfiguredLogin_ShowsGuidanceWithoutAuthenticating(string userName)
    {
        var validator = new Mock<ILdapCredentialValidator>(MockBehavior.Strict);
        var session = new SessionContext();
        var settings = new Mock<IAppSettingsStore>();
        settings.Setup(s => s.Load()).Returns(new AppSettings());
        var sut = new LoginViewModel(validator.Object, session, settings.Object, new Mock<IRememberedUserStore>().Object)
        {
            UserName = userName,
            Password = "test-password"
        };

        var result = await sut.TryLoginAsync();

        Assert.False(result.Success);
        Assert.Contains("Configure o domínio", result.Error);
        Assert.False(sut.IsAuthenticating);
        Assert.Null(session.Credential);
        validator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("usuario.teste@laboratorio.test", "", null, "laboratorio.test", "laboratorio.test")]
    [InlineData("usuario.teste", "laboratorio.test", null, "laboratorio.test", "laboratorio.test")]
    [InlineData("LABORATORIO\\usuario.teste", "laboratorio.test", null, "LABORATORIO", "laboratorio.test")]
    [InlineData("LABORATORIO\\usuario.teste", "", "ldap.laboratorio.test", "LABORATORIO", "ldap.laboratorio.test")]
    public async Task ConfiguredIdentity_UsesExpectedDomainAndHost(
        string userName, string configuredDomain, string? host, string expectedDomain, string expectedHost)
    {
        var validator = new Mock<ILdapCredentialValidator>(MockBehavior.Strict);
        validator.Setup(v => v.ValidateAsync(expectedHost,
                It.Is<NetworkCredential>(c => c.UserName == "usuario.teste" && c.Domain == expectedDomain),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LdapValidationResult.Success());
        var settings = new Mock<IAppSettingsStore>();
        settings.Setup(s => s.Load()).Returns(new AppSettings(configuredDomain, host));
        var session = new SessionContext();
        var sut = new LoginViewModel(validator.Object, session, settings.Object, new Mock<IRememberedUserStore>().Object)
        {
            UserName = userName,
            Password = "test-password"
        };

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedDomain, session.DomainName);
        Assert.False(sut.IsAuthenticating);
        validator.VerifyAll();
    }
}
