using PrinterInstall.App.Services;

namespace PrinterInstall.App.Tests.Services;

public class RememberedUserStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _filePath;

    public RememberedUserStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "remembered-user.json");
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
            // best effort cleanup
        }
    }

    private RememberedUserStore CreateSut() => new(_filePath);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.Load());
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSameUser()
    {
        var sut = CreateSut();
        var expected = new RememberedUser("preventsenior.local", "admin.user");

        sut.Save(expected);
        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(expected.DomainName, loaded.DomainName);
        Assert.Equal(expected.UserName, loaded.UserName);
    }

    [Fact]
    public void Clear_AfterSave_LoadReturnsNull()
    {
        var sut = CreateSut();
        sut.Save(new RememberedUser("preventsenior.local", "admin.user"));

        sut.Clear();

        Assert.Null(sut.Load());
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Save_OverwritesPrevious()
    {
        var sut = CreateSut();
        sut.Save(new RememberedUser("preventsenior.local", "user.a"));
        sut.Save(new RememberedUser("preventsenior.local", "user.b"));

        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal("user.b", loaded.UserName);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsNullAndDeletesFile()
    {
        File.WriteAllText(_filePath, "{ not valid json");
        var sut = CreateSut();

        var loaded = sut.Load();

        Assert.Null(loaded);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Load_WhenFieldsEmpty_ReturnsNullAndDeletesFile()
    {
        File.WriteAllText(_filePath, """{"domainName":"","userName":"x"}""");
        var sut = CreateSut();

        Assert.Null(sut.Load());
        Assert.False(File.Exists(_filePath));
    }
}
