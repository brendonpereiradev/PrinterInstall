namespace PrinterInstall.Core.Models;

public sealed class PrinterControlTarget
{
    public required string ComputerName { get; init; }
    public IReadOnlyList<PrinterRenameItem> Renames { get; init; } = Array.Empty<PrinterRenameItem>();
    public IReadOnlyList<PrinterRemovalQueueItem> QueuesToRemove { get; init; } = Array.Empty<PrinterRemovalQueueItem>();
}
