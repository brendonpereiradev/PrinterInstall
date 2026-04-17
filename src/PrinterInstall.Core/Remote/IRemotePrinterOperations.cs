using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemotePrinterOperations
{
    Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default);

    Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default);

    Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default);
}
