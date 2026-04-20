using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Drivers;

public class LocalDriverPackageCatalogTests
{
    private static string CreateTempDriverTree(string brandFolderName, Action<string> populate)
    {
        var root = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        var brandRoot = Path.Combine(root, "Drivers", brandFolderName);
        Directory.CreateDirectory(brandRoot);
        populate(brandRoot);
        return root;
    }

    [Fact]
    public void TryGet_WhenInfInTopLevel_ReturnsPackage()
    {
        var root = CreateTempDriverTree("Gainscha", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "Gprinter.inf"), "");
            File.WriteAllText(Path.Combine(brand, "Gprinter.cat"), "");
            Directory.CreateDirectory(Path.Combine(brand, "x64"));
        });
        var sut = new LocalDriverPackageCatalog(root);

        var pkg = sut.TryGet(PrinterBrand.Gainscha);

        Assert.NotNull(pkg);
        Assert.Equal(PrinterBrand.Gainscha, pkg!.Brand);
        Assert.Equal("Gprinter.inf", pkg.InfFileName);
        Assert.Equal(Path.Combine(root, "Drivers", "Gainscha"), pkg.RootFolder);
        Assert.Equal("Gainscha GA-2408T", pkg.ExpectedDriverName);
    }

    [Fact]
    public void TryGet_WhenBrandFolderMissing_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Epson));
    }

    [Fact]
    public void TryGet_WhenBrandFolderHasNoInfAtTopLevel_ReturnsNull()
    {
        var root = CreateTempDriverTree("Epson", brand =>
        {
            Directory.CreateDirectory(Path.Combine(brand, "WINX64"));
            File.WriteAllText(Path.Combine(brand, "WINX64", "nested.inf"), "");
            File.WriteAllText(Path.Combine(brand, "readme.txt"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Epson));
    }

    [Fact]
    public void TryGet_IgnoresCatAndPnfWhenLookingForInf()
    {
        var root = CreateTempDriverTree("Lexmark", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "foo.cat"), "");
            File.WriteAllText(Path.Combine(brand, "foo.pnf"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Lexmark));
    }

    [Fact]
    public void TryGet_PicksFirstInfAlphabeticallyWhenMultiple()
    {
        var root = CreateTempDriverTree("Lexmark", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "BBB.inf"), "");
            File.WriteAllText(Path.Combine(brand, "AAA.inf"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        var pkg = sut.TryGet(PrinterBrand.Lexmark);

        Assert.NotNull(pkg);
        Assert.Equal("AAA.inf", pkg!.InfFileName);
    }
}
