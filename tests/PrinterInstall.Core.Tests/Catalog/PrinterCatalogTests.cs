using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Catalog;

public class PrinterCatalogTests
{
    [Fact]
    public void GetExpectedDriverName_Epson_ReturnsUniversalName()
    {
        var name = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        Assert.Equal("EPSON Universal Print Driver", name);
    }

    [Fact]
    public void GetExpectedDriverName_LexmarkAndGainscha_ReturnsCatalogNames()
    {
        Assert.Equal("Lexmark Universal v4 XL", PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark));
        Assert.Equal("Gainscha GA-2408T", PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha));
    }
}
