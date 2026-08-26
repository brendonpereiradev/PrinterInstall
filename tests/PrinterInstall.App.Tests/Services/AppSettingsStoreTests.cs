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

    private AppSettingsStore CreateSut(string defaultDomain = "preventsenior.local") => new(_filePath, defaultDomain);

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultDomain()
    {
        var sut = CreateSut("meudominio.local");
        var settings = sut.Load();

        Assert.Equal("meudominio.local", settings.DomainName);
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
        var expected = new AppSettings("novodominio.local", null);

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.Equal("novodominio.local", loaded.DomainName);
        Assert.Null(loaded.LdapHost);
    }

    [Fact]
    public void ResetToDefaults_DeletesFileAndReturnsDefaults()
    {
        var sut = CreateSut("padrao.local");
        sut.Save(new AppSettings("custom.local", "10.0.0.1"));
        Assert.True(File.Exists(_filePath));

        sut.ResetToDefaults();

        Assert.False(File.Exists(_filePath));
        var loaded = sut.Load();
        Assert.Equal("padrao.local", loaded.DomainName);
        Assert.Null(loaded.LdapHost);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaultsAndCleansUp()
    {
        File.WriteAllText(_filePath, "{ json invalido !!!");
        var sut = CreateSut("padrao.local");

        var loaded = sut.Load();

        Assert.Equal("padrao.local", loaded.DomainName);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Load_WhenDomainEmpty_ReturnsDefaultsAndCleansUp()
    {
        File.WriteAllText(_filePath, """{"domainName":"   ","ldapHost":"10.0.0.1"}""");
        var sut = CreateSut("padrao.local");

        var loaded = sut.Load();

        Assert.Equal("padrao.local", loaded.DomainName);
        Assert.False(File.Exists(_filePath));
    }
}
