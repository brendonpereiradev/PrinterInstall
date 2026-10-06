using System.ComponentModel;
using System.Net.Sockets;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapLoginErrorMessagesTests
{
    [Theory]
    [InlineData(1330, LoginFailureKind.PasswordExpired, "A senha expirou e precisa ser alterada.")]
    [InlineData(1331, LoginFailureKind.AccountDisabled, "Conta de usuário desabilitada.")]
    [InlineData(1793, LoginFailureKind.AccountExpired, "Conta de usuário expirada.")]
    [InlineData(1907, LoginFailureKind.PasswordMustChange, "A senha precisa ser alterada antes de entrar.")]
    [InlineData(1909, LoginFailureKind.AccountLocked, "Conta de usuário bloqueada.")]
    public void FromWin32Error_AccountStatus_HasCorrectMeaning(int code, LoginFailureKind kind, string message)
    {
        Assert.Equal(kind, LdapLoginErrorMessages.KindFromWin32Error(code));
        Assert.Equal(message, LdapLoginErrorMessages.FromWin32Error(code));
    }

    [Theory]
    [InlineData(1326, LoginFailureKind.InvalidCredentials)]
    [InlineData(1909, LoginFailureKind.AccountLocked)]
    [InlineData(53, LoginFailureKind.NetworkUnavailable)]
    public void KindFromException_SmbWin32Exception_UsesNativeCodeRegardlessOfLanguage(int code, LoginFailureKind kind)
    {
        Assert.Equal(kind, LdapLoginErrorMessages.KindFromException(new Win32Exception(code, "opaque detail")));
    }

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

    [Theory]
    [InlineData(0x33)]
    [InlineData(0x34)]
    [InlineData(0x51)]
    public void KindFromLdapErrorCode_ServerBusyOrUnavailable_DoesNotReportInvalidCredentials(int code)
    {
        Assert.Equal(LoginFailureKind.NetworkUnavailable, LdapLoginErrorMessages.KindFromLdapErrorCode(code));
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
