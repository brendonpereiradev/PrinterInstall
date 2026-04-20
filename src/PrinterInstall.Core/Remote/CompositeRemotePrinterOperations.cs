using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Tenta executar via WinRM; em caso de falha, faz fallback para WMI/CIM.
/// Aplica-se a todas as operações remotas (listar drivers, criar porta TCP/IP e adicionar impressora).
/// </summary>
public sealed class CompositeRemotePrinterOperations : IRemotePrinterOperations
{
    private readonly IRemotePrinterOperations _primary;
    private readonly IRemotePrinterOperations _fallback;

    public CompositeRemotePrinterOperations(IRemotePrinterOperations primary, IRemotePrinterOperations fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primary.GetInstalledDriverNamesAsync(computerName, credential, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await _fallback.GetInstalledDriverNamesAsync(computerName, credential, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.CreateTcpPrinterPortAsync(computerName, credential, portName, printerHostAddress, portNumber, protocol, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.CreateTcpPrinterPortAsync(computerName, credential, portName, printerHostAddress, portNumber, protocol, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.AddPrinterAsync(computerName, credential, printerName, driverName, portName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.AddPrinterAsync(computerName, credential, printerName, driverName, portName, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RemotePrinterQueueInfo>? primaryList = null;
        Exception? primaryError = null;

        try
        {
            primaryList = await _primary.ListPrinterQueuesAsync(computerName, credential, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            primaryError = ex;
        }

        if (primaryList is { Count: > 0 })
            return primaryList;

        try
        {
            var fallbackList = await _fallback.ListPrinterQueuesAsync(computerName, credential, cancellationToken).ConfigureAwait(false);
            if (fallbackList.Count > 0)
                return fallbackList;

            if (primaryError is not null)
                throw new InvalidOperationException($"Failed to list printers via WinRM: {primaryError.Message}", primaryError);

            return Array.Empty<RemotePrinterQueueInfo>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (primaryError is not null)
                throw new InvalidOperationException(
                    $"Failed to list printers (WinRM: {primaryError.Message}; WMI: {ex.Message})",
                    primaryError);

            throw;
        }
    }

    public async Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.RemovePrinterQueueAsync(computerName, credential, printerName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.RemovePrinterQueueAsync(computerName, credential, printerName, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primary.CountPrintersUsingPortAsync(computerName, credential, portName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await _fallback.CountPrintersUsingPortAsync(computerName, credential, portName, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.RemoveTcpPrinterPortAsync(computerName, credential, portName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.RemoveTcpPrinterPortAsync(computerName, credential, portName, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.InstallPrinterDriverAsync(computerName, credential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface why the primary channel failed so the operator can tell
            // the difference between "WinRM unavailable" and "Add-PrinterDriver
            // refused the package". The exception message carries the captured
            // PowerShell output from our updated PowerShellInvoker.
            log?.Report("WINRM>> Primary install failed, falling back to CIM. " + Summarize(ex.Message));
            await _fallback.InstallPrinterDriverAsync(computerName, credential, package, log, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Summarize(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;
        const int max = 800;
        return message.Length <= max ? message : message[..max] + "…";
    }
}
