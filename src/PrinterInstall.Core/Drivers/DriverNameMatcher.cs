namespace PrinterInstall.Core.Drivers;

public static class DriverNameMatcher
{
    public static bool IsDriverInstalled(IEnumerable<string> installedDriverNames, string expectedDriverName)
    {
        var expected = expectedDriverName.Trim();
        foreach (var name in installedDriverNames)
        {
            if (string.Equals(name.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
