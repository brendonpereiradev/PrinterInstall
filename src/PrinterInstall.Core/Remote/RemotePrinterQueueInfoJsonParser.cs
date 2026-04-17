using System.Text.Json;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public static class RemotePrinterQueueInfoJsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
