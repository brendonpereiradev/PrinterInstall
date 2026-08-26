using System.Net;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.Tests.Services;

/// <summary>
/// Testes unitários para <see cref="SessionContext"/>.
/// </summary>
public class SessionContextTests
{
    [Fact]
    public void InitialState_HasNullCredentialAndEmptyDomain()
    {
        // Arrange & Act
        var sut = new SessionContext();

        // Assert
        Assert.Null(sut.Credential);
        Assert.Equal(string.Empty, sut.DomainName);
    }

    [Fact]
    public void Setters_UpdatePropertiesCorrectly()
    {
        // Arrange
        var sut = new SessionContext();
        var credential = new NetworkCredential("usuario.admin", "senhaSegura123", "PREVENTSENIOR");

        // Act
        sut.Credential = credential;
        sut.DomainName = "PREVENTSENIOR";

        // Assert
        Assert.Same(credential, sut.Credential);
        Assert.Equal("usuario.admin", sut.Credential.UserName);
        Assert.Equal("senhaSegura123", sut.Credential.Password);
        Assert.Equal("PREVENTSENIOR", sut.Credential.Domain);
        Assert.Equal("PREVENTSENIOR", sut.DomainName);
    }
}
