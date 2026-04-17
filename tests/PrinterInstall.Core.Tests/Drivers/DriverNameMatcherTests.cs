using PrinterInstall.Core.Drivers;

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
}
