using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterControlOrchestrator
{
    private readonly IRemotePrinterOperations _remote;

    public PrinterControlOrchestrator(IRemotePrinterOperations remote)
    {
        _remote = remote;
    }

    public async Task RunAsync(PrinterControlRequest request, IProgress<PrinterRemovalProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var target in request.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var computer = target.ComputerName;

            if (target.Renames.Count == 0 && target.QueuesToRemove.Count == 0)
            {
                progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.TargetCompleted, "Nothing to do"));
                continue;
            }

            progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.ContactingRemote, "Starting..."));

            var orderedRenames = target.Renames
                .OrderBy(r => r.CurrentName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rename in orderedRenames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RenamingQueue,
                    $"Renaming '{rename.CurrentName}' to '{rename.NewName}'...",
                    PrinterQueueName: rename.CurrentName));

                try
                {
                    await _remote.RenamePrinterQueueAsync(
                        computer, request.DomainCredential, rename.CurrentName, rename.NewName, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.Error,
                        $"Failed to rename '{rename.CurrentName}': {Flatten(ex)}",
                        PrinterQueueName: rename.CurrentName));
                }
            }

            var orderedRemovals = target.QueuesToRemove
                .OrderBy(q => q.PrinterName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in orderedRemovals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RemovingQueue,
                    $"Removing '{item.PrinterName}'...",
                    PrinterQueueName: item.PrinterName,
                    PortName: item.PortName));

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
                        computer,
                        PrinterRemovalProgressState.Error,
                        $"Failed to remove '{item.PrinterName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.PortName))
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Queue removed.",
                        PrinterQueueName: item.PrinterName));
                    continue;
                }

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
                        computer,
                        PrinterRemovalProgressState.Warning,
                        $"Could not check port usage for '{item.PortName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                if (count != 0)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Queue removed; port still in use.",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                progress.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RemovingOrphanPort,
                    $"Removing orphan port '{item.PortName}'...",
                    PrinterQueueName: item.PrinterName,
                    PortName: item.PortName));

                try
                {
                    await _remote.RemoveTcpPrinterPortAsync(computer, request.DomainCredential, item.PortName!, cancellationToken).ConfigureAwait(false);
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Queue and port removed.",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.Warning,
                        $"Could not remove orphan port '{item.PortName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
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
