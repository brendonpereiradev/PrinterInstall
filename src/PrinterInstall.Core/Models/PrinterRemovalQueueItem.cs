namespace PrinterInstall.Core.Models;

/// <summary>
/// One printer queue to remove on a target machine.
/// </summary>
/// <param name="PrinterName">Exact name of the Windows print queue to remove.</param>
/// <param name="PortName">
/// Name of the TCP/IP port currently bound to the queue. <c>null</c> or empty means
/// "no port / skip the orphan-port cleanup step" for this queue.
/// </param>
public sealed record PrinterRemovalQueueItem(string PrinterName, string? PortName);
