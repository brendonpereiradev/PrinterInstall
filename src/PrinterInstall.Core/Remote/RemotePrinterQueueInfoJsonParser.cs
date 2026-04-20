using System.Text.Json;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public static class RemotePrinterQueueInfoJsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// PowerShell may emit a BOM, verbose prefix lines, or split output across records.
    /// Picks the first line that looks like JSON or joins lines as a last resort.
    /// </summary>
    public static string? NormalizeInvokerLinesToJson(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return null;

        if (lines.Count == 1)
            return SanitizeLine(lines[0]);

        foreach (var line in lines)
        {
            var s = SanitizeLine(line);
            if (s is { Length: > 0 } && (s[0] == '[' || s[0] == '{'))
                return s;
        }

        var joined = SanitizeLine(string.Join(string.Empty, lines));
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static string? SanitizeLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        return line.Trim().TrimStart('\uFEFF');
    }

    public static IReadOnlyList<RemotePrinterQueueInfo> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RemotePrinterQueueInfo>();

        json = json.Trim();
        if (json == "null" || json == "[]")
            return Array.Empty<RemotePrinterQueueInfo>();

        try
        {
            if (json.StartsWith('['))
                return JsonSerializer.Deserialize<List<RemotePrinterQueueInfo>>(json, Options) ?? new List<RemotePrinterQueueInfo>();

            var single = JsonSerializer.Deserialize<RemotePrinterQueueInfo>(json, Options);
            return single is null ? Array.Empty<RemotePrinterQueueInfo>() : new[] { single };
        }
        catch (JsonException)
        {
            return Array.Empty<RemotePrinterQueueInfo>();
        }
    }
}
