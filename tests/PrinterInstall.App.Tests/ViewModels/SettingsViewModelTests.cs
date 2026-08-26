using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly Mock<IAppSettingsStore> _mockSettingsStore = new();
    private readonly Mock<IDomainDetector> _mockDomainDetector = new();

    private SettingsViewModel CreateSut(AppSettings? initialSettings = null)
    {
        _mockSettingsStore.Setup(s => s.Load())
            .Returns(initialSettings ?? new AppSettings("padrao.local", null));

        return new SettingsViewModel(_mockSettingsStore.Object, _mockDomainDetector.Object);
    }

    [Fact]
    public void Initialize_LoadsSettingsFromStore()
    {
        var sut = CreateSut(new AppSettings("empresa.local", "192.168.1.10"));

        Assert.Equal("empresa.local", sut.DomainName);
        Assert.Equal("192.168.1.10", sut.LdapHost);
        Assert.False(sut.IsSaved);
        Assert.Null(sut.StatusMessage);
    }

    [Fact]
    public void DetectDomain_WhenDetected_UpdatesDomainNameAndStatus()
    {
        _mockDomainDetector.Setup(d => d.DetectCurrentDomain())
            .Returns("detectado.com.br");

        var sut = CreateSut();
        sut.DetectDomain();

        Assert.Equal("detectado.com.br", sut.DomainName);
        Assert.True(sut.IsSuccessMessage);
        Assert.Contains("detectado.com.br", sut.StatusMessage);
    }

    [Fact]
    public void DetectDomain_WhenNotDetected_ShowsInformationalMessage()
    {
        _mockDomainDetector.Setup(d => d.DetectCurrentDomain())
            .Returns((string?)null);

        var sut = CreateSut();
        sut.DetectDomain();

        Assert.False(sut.IsSuccessMessage);
        Assert.Contains("Nenhum domínio", sut.StatusMessage);
    }

    [Fact]
    public void TrySave_WhenDomainEmpty_ReturnsFalseAndSetsErrorMessage()
    {
        var sut = CreateSut();
        sut.DomainName = "   ";

        var saved = sut.TrySave();

        Assert.False(saved);
        Assert.False(sut.IsSaved);
        Assert.False(sut.IsSuccessMessage);
        Assert.Contains("obrigatório", sut.StatusMessage);
        _mockSettingsStore.Verify(s => s.Save(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public void TrySave_WhenValid_SavesToStoreAndSetsSuccessMessage()
    {
        var sut = CreateSut();
        sut.DomainName = "novo.dominio.local";
        sut.LdapHost = "  10.0.0.5  ";

        var saved = sut.TrySave();

        Assert.True(saved);
        Assert.True(sut.IsSaved);
        Assert.True(sut.IsSuccessMessage);
        _mockSettingsStore.Verify(s => s.Save(new AppSettings("novo.dominio.local", "10.0.0.5")), Times.Once);
    }

    [Fact]
    public void ResetToDefaults_CallsResetAndReloads()
    {
        var sut = CreateSut(new AppSettings("personalizado.local", "1.1.1.1"));
        _mockSettingsStore.Setup(s => s.Load())
            .Returns(new AppSettings("padrao.local", null));

        sut.ResetToDefaults();

        _mockSettingsStore.Verify(s => s.ResetToDefaults(), Times.Once);
        Assert.Equal("padrao.local", sut.DomainName);
        Assert.Equal("", sut.LdapHost);
        Assert.True(sut.IsSuccessMessage);
    }
}
