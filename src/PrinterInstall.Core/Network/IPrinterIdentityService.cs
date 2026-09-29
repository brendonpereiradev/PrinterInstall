using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public interface IPrinterIdentityService
{
    Task<PrinterIdentityResult> IdentifyAsync(string host, CancellationToken cancellationToken = default);
}
