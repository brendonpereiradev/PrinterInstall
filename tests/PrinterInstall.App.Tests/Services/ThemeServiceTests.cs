using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using Wpf.Ui.Appearance;

namespace PrinterInstall.App.Tests.Services;

public class ThemeServiceTests
{
    private readonly Mock<IAppSettingsStore> _storeMock = new();

    [Fact]
    public void Constructor_WhenSettingsStoreHasLight_InitializesAsLight()
    {
        _storeMock.Setup(s => s.Load()).Returns(new AppSettings(Theme: "Light"));

        var sut = new ThemeService(_storeMock.Object);

        Assert.Equal(ApplicationTheme.Light, sut.CurrentTheme);
        Assert.False(sut.IsDarkMode);
    }

    [Fact]
    public void Constructor_WhenSettingsStoreHasDark_InitializesAsDark()
    {
        _storeMock.Setup(s => s.Load()).Returns(new AppSettings(Theme: "Dark"));

        var sut = new ThemeService(_storeMock.Object);

        Assert.Equal(ApplicationTheme.Dark, sut.CurrentTheme);
        Assert.True(sut.IsDarkMode);
    }

    [Fact]
    public void ToggleTheme_WhenLight_SwitchesToDarkAndPersists()
    {
        var initialSettings = new AppSettings("meudominio.local", null, "Light");
        _storeMock.Setup(s => s.Load()).Returns(initialSettings);

        var sut = new ThemeService(_storeMock.Object);
        var eventFired = false;
        ApplicationTheme? eventTheme = null;
        sut.ThemeChanged += (_, theme) =>
        {
            eventFired = true;
            eventTheme = theme;
        };

        sut.ToggleTheme();

        Assert.Equal(ApplicationTheme.Dark, sut.CurrentTheme);
        Assert.True(sut.IsDarkMode);
        Assert.True(eventFired);
        Assert.Equal(ApplicationTheme.Dark, eventTheme);

        _storeMock.Verify(s => s.Save(It.Is<AppSettings>(cfg => cfg.Theme == "Dark")), Times.Once);
    }

    [Fact]
    public void ToggleTheme_WhenDark_SwitchesToLightAndPersists()
    {
        var initialSettings = new AppSettings("meudominio.local", null, "Dark");
        _storeMock.Setup(s => s.Load()).Returns(initialSettings);

        var sut = new ThemeService(_storeMock.Object);

        sut.ToggleTheme();

        Assert.Equal(ApplicationTheme.Light, sut.CurrentTheme);
        Assert.False(sut.IsDarkMode);

        _storeMock.Verify(s => s.Save(It.Is<AppSettings>(cfg => cfg.Theme == "Light")), Times.Once);
    }

    [Fact]
    public void SetTheme_WithPersistFalse_DoesNotCallSave()
    {
        _storeMock.Setup(s => s.Load()).Returns(new AppSettings(Theme: "Light"));

        var sut = new ThemeService(_storeMock.Object);
        sut.SetTheme(ApplicationTheme.Dark, persist: false);

        Assert.Equal(ApplicationTheme.Dark, sut.CurrentTheme);
        _storeMock.Verify(s => s.Save(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public void Initialize_ExecutesWithoutExceptions()
    {
        _storeMock.Setup(s => s.Load()).Returns(new AppSettings(Theme: "Dark"));

        var sut = new ThemeService(_storeMock.Object);
        sut.Initialize();

        Assert.Equal(ApplicationTheme.Dark, sut.CurrentTheme);
        Assert.True(sut.IsDarkMode);
    }
}

