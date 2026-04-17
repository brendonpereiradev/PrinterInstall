using System.Text.Json.Serialization;

namespace PrinterInstall.Core.Models;

public sealed record RemotePrinterQueueInfo(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("PortName")] string? PortName);
