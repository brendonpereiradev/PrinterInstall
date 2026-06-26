using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public interface IDirectRawPrinterTestService
{
    Task<DirectRawPrinterTestResult> RunAsync(
        string host,
        PrinterBrand brand,
        GainschaLabelPreset? gainschaLabelPreset = null,
        CancellationToken cancellationToken = default);
}
