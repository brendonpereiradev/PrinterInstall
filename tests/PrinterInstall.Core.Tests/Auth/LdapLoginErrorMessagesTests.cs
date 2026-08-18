using System.Net.Sockets;
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

    [Fact]
    public void FromException_SocketException_ReturnsNetworkMessage()
    {
        var message = LdapLoginErrorMessages.FromException(new SocketException((int)SocketError.HostUnreachable));

        Assert.Contains("Não foi possível contatar o servidor LDAP do domínio.", message);
        Assert.Contains("389", message);
    }

    [Fact]
    public void FromWin32Error_InvalidCredentials_ReturnsPortugueseMessage()
    {
        Assert.Equal("Usuário ou senha inválidos.", LdapLoginErrorMessages.FromWin32Error(1326));
    }

    [Fact]
    public void FromException_GenericException_IncludesDetails()
    {
        var message = LdapLoginErrorMessages.FromException(new InvalidOperationException("detalhe tecnico"));

        Assert.Contains("Falha ao autenticar no domínio.", message);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("detalhe tecnico", message);
    }
}
