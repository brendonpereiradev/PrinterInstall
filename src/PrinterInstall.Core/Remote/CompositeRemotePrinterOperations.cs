using System.Net;

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
}
