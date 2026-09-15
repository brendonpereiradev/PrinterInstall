using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;
using Wpf.Ui.Appearance;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelThemeTests
{
    private readonly Mock<ILdapCredentialValidator> _ldapMock = new();
    private readonly Mock<ISessionContext> _sessionMock = new();
    private readonly Mock<IAppSettingsStore> _settingsStoreMock = new();
    private readonly Mock<IRememberedUserStore> _rememberedUserMock = new();
    private readonly Mock<IThemeService> _themeServiceMock = new();

    private LoginViewModel CreateSut()
    {
        return new LoginViewModel(
            _ldapMock.Object,
            _sessionMock.Object,
            _settingsStoreMock.Object,
            _rememberedUserMock.Object,
            _themeServiceMock.Object);
    }

    [Fact]
    public void Constructor_InitializesIsDarkModeFromThemeService()
    {
        _themeServiceMock.SetupGet(t => t.IsDarkMode).Returns(true);

        var sut = CreateSut();

        Assert.True(sut.IsDarkMode);
    }

    [Fact]
    public void ToggleThemeCommand_CallsThemeServiceToggleTheme()
    {
        _themeServiceMock.SetupGet(t => t.IsDarkMode).Returns(false);
        var sut = CreateSut();

        _themeServiceMock.Setup(t => t.ToggleTheme()).Callback(() =>
        {
            _themeServiceMock.SetupGet(t => t.IsDarkMode).Returns(true);
        });

        sut.ToggleThemeCommand.Execute(null);

        _themeServiceMock.Verify(t => t.ToggleTheme(), Times.Once);
        Assert.True(sut.IsDarkMode);
    }
}
