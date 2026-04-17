namespace PrinterInstall.Core.Models;

public sealed class PrinterRemovalTarget
{
    public required string ComputerName { get; init; }
    public required IReadOnlyList<PrinterRemovalQueueItem> QueuesToRemove { get; init; }
}
