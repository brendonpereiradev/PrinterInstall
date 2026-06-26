using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public interface IGainschaLabelPreferenceConfigurator
{
    Task ApplyAsync(string printerQueueName, GainschaLabelPreset preset, CancellationToken cancellationToken = default);
}
