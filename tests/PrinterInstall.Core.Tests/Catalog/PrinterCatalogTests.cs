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
    public void GetModels_ContainsAtLeastOnePerBrand()
    {
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Epson));
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Lexmark));
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Gainscha));
    }
}
