using System.Net;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class DeploymentRollbackRunner
{
    private readonly IRemotePrinterOperations _remote;
    private readonly PrinterControlOrchestrator _control;

    public DeploymentRollbackRunner(IRemotePrinterOperations remote, PrinterControlOrchestrator control)
    {
        _remote = remote;
        _control = control;
    }

    public async Task RunAsync(
        DeploymentRollbackJournal journal,
        NetworkCredential domainCredential,
        IProgress<PrinterRemovalProgressEvent>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!journal.HasRollbackWork)
            return;

        var progressSink = progress ?? new Progress<PrinterRemovalProgressEvent>(_ => { });

        var targetsByComputer = journal.QueueEntries
            .GroupBy(e => e.ComputerName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PrinterControlTarget
            {
                ComputerName = g.Key,
                QueuesToRemove = g.Select(e => new PrinterRemovalQueueItem(e.PrinterName, e.PortName)).ToList()
            })
            .ToList();

        var request = new PrinterControlRequest
        {
            DomainCredential = domainCredential,
            Targets = targetsByComputer
        };

        if (targetsByComputer.Count > 0)
            await _control.RunAsync(request, progressSink, cancellationToken).ConfigureAwait(false);

        foreach (var (computer, portName) in journal.PortOnlyEntries
                     .OrderBy(x => x.Computer, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.PortName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            progressSink.Report(new PrinterRemovalProgressEvent(
                computer,
                PrinterRemovalProgressState.RemovingOrphanPort,
                $"Reversão: verificando porta órfã '{portName}'...",
                PortName: portName));

            int count;
            try
            {
                count = await _remote.CountPrintersUsingPortAsync(computer, domainCredential, portName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                progressSink.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.Warning,
                    $"Reversão: não foi possível verificar porta '{portName}': {ex.Message}",
                    PortName: portName));
                continue;
            }

            if (count != 0)
            {
                progressSink.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RollbackSucceeded,
                    $"Reversão: porta '{portName}' mantida (em uso por {count} impressora(s)).",
                    PortName: portName));
                continue;
            }

            try
            {
                await _remote.RemoveTcpPrinterPortAsync(computer, domainCredential, portName, cancellationToken).ConfigureAwait(false);
                progressSink.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.RollbackSucceeded,
                    "Porta órfã removida com sucesso.",
                    PortName: portName));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                progressSink.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.Warning,
                    $"Reversão: não foi possível remover porta '{portName}': {ex.Message}",
                    PortName: portName));
            }
        }
    }
}
