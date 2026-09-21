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
                progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.TargetCompleted, "Nenhuma alteração pendente."));
                continue;
            }

            progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.ContactingRemote, "Iniciando..."));

            var orderedRenames = target.Renames
                .OrderBy(r => r.CurrentName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rename in orderedRenames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RenamingQueue,
                    $"Renomeando '{rename.CurrentName}' para '{rename.NewName}'...",
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
                        $"Falha ao renomear '{rename.CurrentName}': {Flatten(ex)}",
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
                    $"Removendo fila '{item.PrinterName}'...",
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
                        $"Falha ao remover '{item.PrinterName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.PortName))
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Fila removida com sucesso.",
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
                        $"Não foi possível verificar uso da porta '{item.PortName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                if (count != 0)
                {
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Fila removida (porta em uso por outra impressora).",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                    continue;
                }

                progress.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RemovingOrphanPort,
                    $"Removendo porta órfã '{item.PortName}'...",
                    PrinterQueueName: item.PrinterName,
                    PortName: item.PortName));

                try
                {
                    await _remote.RemoveTcpPrinterPortAsync(computer, request.DomainCredential, item.PortName!, cancellationToken).ConfigureAwait(false);
                    progress.Report(new PrinterRemovalProgressEvent(
                        computer,
                        PrinterRemovalProgressState.RollbackSucceeded,
                        "Fila e porta removidas com sucesso.",
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
                        $"Falha ao remover porta órfã '{item.PortName}': {Flatten(ex)}",
                        PrinterQueueName: item.PrinterName,
                        PortName: item.PortName));
                }
            }

            progress.Report(new PrinterRemovalProgressEvent(computer, PrinterRemovalProgressState.TargetCompleted, "Concluído com sucesso."));
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
