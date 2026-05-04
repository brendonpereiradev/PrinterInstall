using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Drivers;

public class DriverNameMatcherTests
{
    [Fact]
    public void IsDriverInstalled_CaseInsensitive_Matches()
    {
        var installed = new[] { "EPSON Universal Print Driver", "Other" };
        Assert.True(DriverNameMatcher.IsDriverInstalled(installed, "epson universal print driver"));
    }

    [Fact]
    public void IsDriverInstalled_Missing_ReturnsFalse()
    {
        Assert.False(DriverNameMatcher.IsDriverInstalled(Array.Empty<string>(), "Lexmark Universal v4 XL"));
    }

    [Fact]
    public void IsAnyAcceptedDriverInstalled_LexmarkV2Only_ReturnsTrue()
    {
        var installed = new[] { "Lexmark Universal v2 XL", "Other" };
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
        Assert.True(DriverNameMatcher.IsAnyAcceptedDriverInstalled(installed, order));
    }

    [Fact]
    public void ResolveInstalledDriverName_BothV2AndV4_ReturnsV4()
    {
        var installed = new[] { "Lexmark Universal v2 XL", "Lexmark Universal v4 XL" };
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
        Assert.Equal("Lexmark Universal v4 XL", DriverNameMatcher.ResolveInstalledDriverName(installed, order));
    }

    [Fact]
    public void ResolveInstalledDriverName_OnlyV2_ReturnsV2()
    {
        var installed = new[] { "Lexmark Universal v2 XL" };
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
        Assert.Equal("Lexmark Universal v2 XL", DriverNameMatcher.ResolveInstalledDriverName(installed, order));
    }

    [Fact]
    public void ResolveInstalledDriverName_None_ReturnsNull()
    {
        var installed = new[] { "Other" };
        var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
        Assert.Null(DriverNameMatcher.ResolveInstalledDriverName(installed, order));
    }
}
