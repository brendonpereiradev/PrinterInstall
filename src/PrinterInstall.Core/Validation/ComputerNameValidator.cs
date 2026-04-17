using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Validation;

public static partial class ComputerNameValidator
{
    [GeneratedRegex(@"^[\w.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    public static bool IsPlausibleComputerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 253)
            return false;
        return HostPattern().IsMatch(name.Trim());
    }
}
