using System.IO;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.Tests.Services;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _filePath;

    public AppSettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Limpeza best effort
        }
    }

    private AppSettingsStore CreateSut(string defaultDomain = "laboratorio.test") => new(_filePath, defaultDomain);

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultDomain()
    {
        var sut = CreateSut("meudominio.test");
        var settings = sut.Load();

        Assert.Equal("meudominio.test", settings.DomainName);
        Assert.Null(settings.LdapHost);
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSavedSettings()
    {
        var sut = CreateSut();
        var expected = new AppSettings("corp.empresa.com", "ldap.empresa.com");

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.Equal("corp.empresa.com", loaded.DomainName);
        Assert.Equal("ldap.empresa.com", loaded.LdapHost);
    }

    [Fact]
    public void Save_WithNullLdapHost_SavesAndLoadsCorrectly()
    {
        var sut = CreateSut();
        var expected = new AppSettings("novodominio.test", null);

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.Equal("novodominio.test", loaded.DomainName);
        Assert.Null(loaded.LdapHost);
    }

    [Fact]
    public void ResetToDefaults_DeletesFileAndReturnsDefaults()
    {
        var sut = CreateSut("padrao.test");
        sut.Save(new AppSettings("custom.test", "10.0.0.1"));
        Assert.True(File.Exists(_filePath));

        sut.ResetToDefaults();

        Assert.False(File.Exists(_filePath));
        var loaded = sut.Load();
        Assert.Equal("padrao.test", loaded.DomainName);
        Assert.Null(loaded.LdapHost);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaultsAndCleansUp()
    {
        File.WriteAllText(_filePath, "{ json invalido !!!");
        var sut = CreateSut("padrao.test");

        var loaded = sut.Load();

        Assert.Equal("padrao.test", loaded.DomainName);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Load_WhenDomainEmpty_ReturnsDefaultsAndCleansUp()
    {
        File.WriteAllText(_filePath, """{"domainName":"   ","ldapHost":"10.0.0.1"}""");
        var sut = CreateSut("padrao.test");

        var loaded = sut.Load();

        Assert.Equal("padrao.test", loaded.DomainName);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Save_WithTheme_SavesAndLoadsCorrectly()
    {
        var sut = CreateSut();
        var expected = new AppSettings("empresa.test", "ldap.empresa.test", "Dark");

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.Equal("Dark", loaded.Theme);
    }

    [Fact]
    public void Load_WhenFileHasNoThemeField_DefaultsToLight()
    {
        File.WriteAllText(_filePath, """{"domainName":"empresa.test"}""");
        var sut = CreateSut();

        var loaded = sut.Load();

        Assert.Equal("Light", loaded.Theme);
    }
}
