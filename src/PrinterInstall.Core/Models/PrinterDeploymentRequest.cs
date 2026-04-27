using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterDeploymentRequest
{
    public required IReadOnlyList<string> TargetComputerNames { get; init; }
    public required IReadOnlyList<PrinterQueueDefinition> Printers { get; init; }
    public required NetworkCredential DomainCredential { get; init; }

    /// <summary>
    /// When true, send a Windows test page to the new queue after a successful add. Default false (opt-in in UI).
    /// </summary>
    public bool PrintTestPage { get; init; }
}
