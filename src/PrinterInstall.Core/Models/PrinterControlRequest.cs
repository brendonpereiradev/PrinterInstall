using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterControlRequest
{
    public required NetworkCredential DomainCredential { get; init; }
    public required IReadOnlyList<PrinterControlTarget> Targets { get; init; }
}
