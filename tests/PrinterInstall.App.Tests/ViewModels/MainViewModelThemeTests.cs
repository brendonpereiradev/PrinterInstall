using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using Wpf.Ui.Appearance;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelThemeTests
{
    private readonly Mock<ISessionContext> _sessionMock = new();
    private readonly Mock<IRemotePrinterOperations> _remoteOpsMock = new();
    private readonly Mock<IThemeService> _themeServiceMock = new();

    private MainViewModel CreateSut()
    {
        var orchestrator = new PrinterDeploymentOrchestrator(_remoteOpsMock.Object);
        var controlOrchestrator = new PrinterControlOrchestrator(_remoteOpsMock.Object);
        var rollbackRunner = new DeploymentRollbackRunner(_remoteOpsMock.Object, controlOrchestrator);
        var localMachineIdentity = new LocalMachineIdentity();

        return new MainViewModel(
            _sessionMock.Object,
            orchestrator,
            rollbackRunner,
            null!,
            localMachineIdentity,
            themeService: _themeServiceMock.Object);
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

    [Fact]
    public void ThemeService_ThemeChanged_UpdatesIsDarkModeOnViewModel()
    {
        _themeServiceMock.SetupGet(t => t.IsDarkMode).Returns(false);
        var sut = CreateSut();
        Assert.False(sut.IsDarkMode);

        _themeServiceMock.SetupGet(t => t.IsDarkMode).Returns(true);
        _themeServiceMock.Raise(t => t.ThemeChanged += null, this, ApplicationTheme.Dark);

        Assert.True(sut.IsDarkMode);
    }
}
