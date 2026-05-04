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

    public static bool IsAnyAcceptedDriverInstalled(IEnumerable<string> installedDriverNames, IReadOnlyList<string> preferenceOrder)
    {
        foreach (var candidate in preferenceOrder)
        {
            if (IsDriverInstalled(installedDriverNames, candidate))
                return true;
        }
        return false;
    }

    public static string? ResolveInstalledDriverName(IEnumerable<string> installedDriverNames, IReadOnlyList<string> preferenceOrder)
    {
        foreach (var candidate in preferenceOrder)
        {
            if (IsDriverInstalled(installedDriverNames, candidate))
                return candidate.Trim();
        }
        return null;
    }
}
