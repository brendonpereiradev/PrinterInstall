using System.Net;
using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelConfigurationTests
{
    [Fact]
    public async Task ValidatedIdentity_IsAlsoUsedForDeploymentAndRememberedUser()
    {
        var validator = new Mock<ILdapCredentialValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LdapValidationResult.Success(new NetworkCredential("00000000000", "unused", "LABORATORIO")));
        var settings = new Mock<IAppSettingsStore>();
        settings.Setup(s => s.Load()).Returns(new AppSettings("laboratorio.test"));
        var session = new SessionContext();
        var remembered = new Mock<IRememberedUserStore>();
        var sut = new LoginViewModel(validator.Object, session, settings.Object, remembered.Object)
        {
            UserName = "000.000.000-00", Password = "test-password", RememberMe = true
        };
        Assert.True((await sut.TryLoginAsync()).Success);
        Assert.Equal("00000000000", session.Credential!.UserName);
        Assert.Equal("LABORATORIO", session.Credential.Domain);
        Assert.Equal("test-password", session.Credential.Password);
        remembered.Verify(s => s.Save(It.Is<RememberedUser>(u => u.UserName == "00000000000" && u.DomainName == "LABORATORIO")));
    }

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
