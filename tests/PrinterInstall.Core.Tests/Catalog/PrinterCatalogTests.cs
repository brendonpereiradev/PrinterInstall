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

    [Fact]
    public void GetDriverResolutionOrder_Lexmark_V4ThenV2()
    {
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
        Assert.Equal(2, order.Count);
        Assert.Equal("Lexmark Universal v4 XL", order[0]);
        Assert.Equal("Lexmark Universal v2 XL", order[1]);
    }

    [Fact]
    public void GetDriverResolutionOrder_Epson_SingleEntry()
    {
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Epson);
        Assert.Single(order);
        Assert.Equal("EPSON Universal Print Driver", order[0]);
    }

    [Fact]
    public void DescribeAcceptableDrivers_Lexmark_JoinsWithOr()
    {
        var text = PrinterCatalog.DescribeAcceptableDrivers(PrinterBrand.Lexmark);
        Assert.Equal("Lexmark Universal v4 XL or Lexmark Universal v2 XL", text);
    }

    [Fact]
    public void DescribeAcceptableDrivers_Gainscha_SingleName()
    {
        Assert.Equal("Gainscha GA-2408T", PrinterCatalog.DescribeAcceptableDrivers(PrinterBrand.Gainscha));
    }
}
