using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterRemovalOrchestrator
{
    private readonly IRemotePrinterOperations _remote;

    public PrinterRemovalOrchestrator(IRemotePrinterOperations remote)
    {
        _remote = remote;
    }

    public async Task RunAsync(PrinterRemovalRequest request, IProgress<PrinterRemovalProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var target in request.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var computer = target.ComputerName;

            if (target.QueuesToRemove.Count == 0)
            {
                progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.TargetCompleted, "Nothing to do"));
                continue;
            }

            progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.ContactingRemote, "Starting removal..."));

            var ordered = target.QueuesToRemove
                .OrderBy(q => q.PrinterName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new PrinterRemovalProgressEvent(
                    computer, PrinterRemovalProgressState.RemovingQueue, $"Removing '{item.PrinterName}'..."));

                try
                {
                    await _remote.RemovePrinterQueueAsync(computer, request.DomainCredential, item.PrinterName, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer, PrinterRemovalProgressState.Error,
                        $"Failed to remove '{item.PrinterName}': {Flatten(ex)}"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.PortName))
                    continue;

                int count;
                try
                {
                    count = await _remote.CountPrintersUsingPortAsync(computer, request.DomainCredential, item.PortName!, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer, PrinterRemovalProgressState.Warning,
                        $"Could not check port usage for '{item.PortName}': {Flatten(ex)}"));
                    continue;
                }

                if (count != 0)
                    continue;

                progress.Report(new PrinterRemovalProgressEvent(
                    computer, PrinterRemovalProgressState.RemovingOrphanPort,
                    $"Removing orphan port '{item.PortName}'..."));

                try
                {
                    await _remote.RemoveTcpPrinterPortAsync(computer, request.DomainCredential, item.PortName!, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer, PrinterRemovalProgressState.Warning,
                        $"Could not remove orphan port '{item.PortName}': {Flatten(ex)}"));
                }
            }

            progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.TargetCompleted, "Done."));
        }
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message?.Trim();
            if (!string.IsNullOrEmpty(msg))
                messages.Add(msg);
        }
        return string.Join(" | ", messages);
    }
}
