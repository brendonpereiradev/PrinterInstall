using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Orchestration;

/// <summary>Delega para <see cref="PrinterControlOrchestrator"/> (apenas remoções).</summary>
public sealed class PrinterRemovalOrchestrator
{
    private readonly PrinterControlOrchestrator _control;

    public PrinterRemovalOrchestrator(PrinterControlOrchestrator control)
    {
        _control = control;
    }

    public Task RunAsync(PrinterRemovalRequest request, IProgress<PrinterRemovalProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        var controlRequest = new PrinterControlRequest
        {
            DomainCredential = request.DomainCredential,
            Targets = request.Targets
                .Select(t => new PrinterControlTarget
                {
                    ComputerName = t.ComputerName,
                    QueuesToRemove = t.QueuesToRemove,
                    Renames = Array.Empty<PrinterRenameItem>()
                })
                .ToList()
        };
        return _control.RunAsync(controlRequest, progress, cancellationToken);
    }
}
