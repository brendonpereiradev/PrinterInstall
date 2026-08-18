using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Delega operações para caminho local ou remoto conforme <see cref="LocalMachineIdentity"/>.
/// </summary>
public sealed class RoutingRemotePrinterOperations : IRemotePrinterOperations
{
    private readonly LocalMachineIdentity _identity;
    private readonly IRemotePrinterOperations _local;
    private readonly IRemotePrinterOperations _remote;

    public RoutingRemotePrinterOperations(
        LocalMachineIdentity identity,
        IRemotePrinterOperations local,
        IRemotePrinterOperations remote)
    {
        _identity = identity;
        _local = local;
        _remote = remote;
    }

    private IRemotePrinterOperations Resolve(string computerName) =>
        _identity.IsLocalMachine(computerName) ? _local : _remote;

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
        => Resolve(computerName).GetInstalledDriverNamesAsync(computerName, credential, cancellationToken);

    public Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
        => Resolve(computerName).PrinterQueueExistsAsync(computerName, credential, printerDisplayName, cancellationToken);

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
        => Resolve(computerName).CreateTcpPrinterPortAsync(computerName, credential, portName, printerHostAddress, portNumber, protocol, cancellationToken);

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
        => Resolve(computerName).AddPrinterAsync(computerName, credential, printerName, driverName, portName, cancellationToken);

    public Task ConfigureGainschaLabelPresetAsync(
        string computerName,
        NetworkCredential credential,
        string printerQueueName,
        GainschaLabelPreset preset,
        CancellationToken cancellationToken = default) =>
        Resolve(computerName).ConfigureGainschaLabelPresetAsync(
            computerName, credential, printerQueueName, preset, cancellationToken);

    public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
        => Resolve(computerName).PrintTestPageAsync(computerName, credential, printerQueueName, cancellationToken);

    public Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
        => Resolve(computerName).ListPrinterQueuesAsync(computerName, credential, cancellationToken);

    public Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
        => Resolve(computerName).RemovePrinterQueueAsync(computerName, credential, printerName, cancellationToken);

    public Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
        => Resolve(computerName).RenamePrinterQueueAsync(computerName, credential, currentName, newName, cancellationToken);

    public Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => Resolve(computerName).CountPrintersUsingPortAsync(computerName, credential, portName, cancellationToken);

    public Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => Resolve(computerName).RemoveTcpPrinterPortAsync(computerName, credential, portName, cancellationToken);

    public Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
        => Resolve(computerName).InstallPrinterDriverAsync(computerName, credential, package, log, cancellationToken);
}
