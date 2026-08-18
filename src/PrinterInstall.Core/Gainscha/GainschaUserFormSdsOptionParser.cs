using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Gainscha;

internal static partial class GainschaUserFormSdsOptionParser
{
    internal const uint RegBinary = 3;
    internal const uint RegDword = 4;
    internal const uint RegSz = 1;

    internal static IReadOnlyList<GainschaUserFormDriverDataEntry> ParseUserFormEntries(string sdsContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(sdsContent);

        var entries = new List<GainschaUserFormDriverDataEntry>();

        foreach (Match match in UserFormHexRegex().Matches(sdsContent))
        {
            entries.Add(new GainschaUserFormDriverDataEntry(
                match.Groups["name"].Value,
                RegBinary,
                ParseHexBytes(match.Groups["value"].Value)));
        }

        foreach (Match match in UserFormDwordRegex().Matches(sdsContent))
        {
            var dword = uint.Parse(match.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            entries.Add(new GainschaUserFormDriverDataEntry(
                match.Groups["name"].Value,
                RegDword,
                BitConverter.GetBytes(dword)));
        }

        foreach (Match match in UserFormStringRegex().Matches(sdsContent))
        {
            var text = UnescapeQuotedString(match.Groups["value"].Value);
            entries.Add(new GainschaUserFormDriverDataEntry(
                match.Groups["name"].Value,
                RegSz,
                Encoding.Unicode.GetBytes(text + '\0')));
        }

        return entries;
    }

    private static byte[] ParseHexBytes(string hexBlob)
    {
        var normalized = hexBlob
            .Replace("\\", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

        var tokens = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            bytes[i] = byte.Parse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return bytes;
    }

    private static string UnescapeQuotedString(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal);

    [GeneratedRegex(
        "\"(?<name>User Form:[^\"]+)\"=hex:(?<value>(?:\\\\\\s*\\r?\\n|[^\\r\\n])+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserFormHexRegex();

    [GeneratedRegex(
        "\"(?<name>User Form:[^\"]+)\"=dword:(?<value>[0-9a-fA-F]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserFormDwordRegex();

    [GeneratedRegex(
        "\"(?<name>User Form:[^\"]+)\"=\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserFormStringRegex();
}

internal readonly record struct GainschaUserFormDriverDataEntry(string Name, uint RegistryType, byte[] Data);
