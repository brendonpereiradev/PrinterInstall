using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapLoginErrorMessagesTests
{
    [Fact]
    public void FromLdapErrorCode_InvalidCredentials_ReturnsPortugueseMessage()
    {
        Assert.Equal("Usuário ou senha inválidos.", LdapLoginErrorMessages.FromLdapErrorCode(0x31));
    }

    [Fact]
    public void FromLdapErrorCode_ServerDown_ReturnsPortugueseMessage()
    {
        Assert.Equal(
            "Não foi possível contatar o servidor LDAP do domínio.",
            LdapLoginErrorMessages.FromLdapErrorCode(0x51));
    }

    [Fact]
    public void FromLdapErrorCode_UnknownCode_IncludesHexCode()
    {
        var message = LdapLoginErrorMessages.FromLdapErrorCode(0x99);

        Assert.Contains("Falha ao autenticar no domínio.", message);
        Assert.Contains("0x99", message);
    }
}
