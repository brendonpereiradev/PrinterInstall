using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterRemovalRequest
{
    public required NetworkCredential DomainCredential { get; init; }
    public required IReadOnlyList<PrinterRemovalTarget> Targets { get; init; }
}
