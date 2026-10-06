using System.ComponentModel;
using System.Net;
using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;
using PrinterInstall.Core.Logging;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelErrorTests
{
    [Theory]
    [InlineData(LoginFailureKind.InvalidCredentials, "Usuário ou senha incorretos.", "Confira")]
    [InlineData(LoginFailureKind.NetworkUnavailable, "Não foi possível conectar ao domínio.", "VPN")]
    [InlineData(LoginFailureKind.AccountLocked, "Sua conta está bloqueada.", "suporte")]
    [InlineData(LoginFailureKind.PasswordExpired, "Sua senha expirou.", "Altere")]
    [InlineData(LoginFailureKind.PasswordMustChange, "Você precisa alterar sua senha.", "Altere")]
    [InlineData(LoginFailureKind.Unknown, "Não foi possível entrar.", "suporte")]
    public async Task TryLoginAsync_ShowsFriendlyMessageAndGuidanceWithDetailsCollapsed(
        LoginFailureKind kind, string title, string guidance)
    {
        var sut = CreateSut(LdapValidationResult.Failure("SMB Win32 diagnostic in English", kind));

        var result = await sut.TryLoginAsync();

        Assert.False(result.Success);
        Assert.Equal(title, result.Error);
        Assert.Equal(title, sut.ErrorMessage);
        Assert.Contains(guidance, sut.ErrorGuidance);
        Assert.Equal("SMB Win32 diagnostic in English", sut.ErrorDetails);
        Assert.False(sut.IsErrorDetailsExpanded);
    }

    [Fact]
    public async Task TryLoginAsync_AllFailureKinds_HaveTitleAndActionableGuidance()
    {
        foreach (var kind in Enum.GetValues<LoginFailureKind>())
        {
            var sut = CreateSut(LdapValidationResult.Failure("Technical detail", kind));
            await sut.TryLoginAsync();
            Assert.False(string.IsNullOrWhiteSpace(sut.ErrorMessage));
            Assert.False(string.IsNullOrWhiteSpace(sut.ErrorGuidance));
            if (kind != LoginFailureKind.Unknown)
                Assert.NotEqual("Não foi possível entrar.", sut.ErrorMessage);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditingCredentials_ClearsPreviousFailureAndCollapsesDetails(bool editUser)
    {
        var sut = CreateSut(LdapValidationResult.Failure("Technical detail", LoginFailureKind.InvalidCredentials));
        await sut.TryLoginAsync();
        sut.IsErrorDetailsExpanded = true;

        if (editUser) sut.UserName = "corrected-user";
        else sut.Password = "corrected-password";

        Assert.Null(sut.ErrorMessage);
        Assert.Null(sut.ErrorGuidance);
        Assert.Null(sut.ErrorDetails);
        Assert.False(sut.IsErrorDetailsExpanded);
    }

    [Fact]
    public async Task TryLoginAsync_NewAttempt_CollapsesPreviousDetails()
    {
        var sut = CreateSut(LdapValidationResult.Failure("Technical detail"));
        await sut.TryLoginAsync();
        sut.IsErrorDetailsExpanded = true;

        await sut.TryLoginAsync();

        Assert.NotNull(sut.ErrorDetails);
        Assert.False(sut.IsErrorDetailsExpanded);
    }

    [Fact]
    public async Task TryLoginAsync_EmptyPassword_ShowsLocalValidationWithoutAuthenticating()
    {
        var validator = new Mock<ILdapCredentialValidator>(MockBehavior.Strict);
        var sut = CreateSut(validator.Object);
        sut.Password = "";

        Assert.False((await sut.TryLoginAsync()).Success);
        Assert.Equal("Informe sua senha.", sut.ErrorMessage);
        Assert.Null(sut.ErrorDetails);
        Assert.False(sut.IsAuthenticating);
        validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryLoginAsync_PasswordInDiagnostic_IsRedactedBeforeDisplayingAndLogging()
    {
        var logger = new Mock<IDiagnosticFileLogger>();
        var sut = CreateSut(LdapValidationResult.Failure("Server rejected test-password; Win32 1326"), logger.Object);

        await sut.TryLoginAsync();

        Assert.DoesNotContain("test-password", sut.ErrorDetails);
        Assert.Contains("[SENHA OMITIDA]", sut.ErrorDetails);
        logger.Verify(l => l.RegisterSensitiveValue("test-password"), Times.Once);
        logger.Verify(l => l.LogWarning(It.Is<string>(s => !s.Contains("test-password") && s.Contains("Win32 1326")), "Login", null), Times.Once);
    }

    [Fact]
    public void UnexpectedException_UsesSameFriendlyPresentationAndHidesTechnicalText()
    {
        var sut = CreateSut(LdapValidationResult.Success());

        sut.ShowUnexpectedError(new Win32Exception(1909, "English error involving test-password"));

        Assert.Equal("Sua conta está bloqueada.", sut.ErrorMessage);
        Assert.DoesNotContain("test-password", sut.ErrorDetails);
        Assert.Contains("English error", sut.ErrorDetails);
        Assert.False(sut.IsErrorDetailsExpanded);
    }

    [Fact]
    public async Task TryLoginAsync_PasswordEditedDuringRequest_RedactsTheSubmittedPassword()
    {
        var completion = new TaskCompletionSource<LdapValidationResult>();
        var validator = new Mock<ILdapCredentialValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var sut = CreateSut(validator.Object);
        var pendingLogin = sut.TryLoginAsync();
        sut.Password = "corrected-password";
        completion.SetResult(LdapValidationResult.Failure("Rejected test-password", LoginFailureKind.InvalidCredentials));

        await pendingLogin;

        Assert.Equal("Rejected [SENHA OMITIDA]", sut.ErrorDetails);
    }

    private static LoginViewModel CreateSut(LdapValidationResult result, IDiagnosticFileLogger? logger = null)
    {
        var validator = new Mock<ILdapCredentialValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return CreateSut(validator.Object, logger);
    }

    private static LoginViewModel CreateSut(ILdapCredentialValidator validator, IDiagnosticFileLogger? logger = null)
    {
        var settings = new Mock<IAppSettingsStore>();
        settings.Setup(s => s.Load()).Returns(new AppSettings("test.example"));
        return new LoginViewModel(validator, new SessionContext(), settings.Object,
            new Mock<IRememberedUserStore>().Object, diagnosticLogger: logger)
        {
            UserName = "operator",
            Password = "test-password"
        };
    }
}
