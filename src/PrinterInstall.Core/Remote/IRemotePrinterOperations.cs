using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public interface IRemotePrinterOperations
{
    Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default);

    Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default);

    Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default);

    Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
