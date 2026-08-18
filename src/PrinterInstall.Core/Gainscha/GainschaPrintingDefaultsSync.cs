using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public static class GainschaPrintingDefaultsSync
{
    public static void SyncUserPreferencesToPrintingDefaults(string printerQueueName, string validatedSdsExportContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);
        ArgumentException.ThrowIfNullOrEmpty(validatedSdsExportContent);

        var entries = GainschaUserFormSdsOptionParser.ParseUserFormEntries(validatedSdsExportContent);
        GainschaUserFormRegistryWriter.ApplyUserFormEntries(printerQueueName, entries);
    }

    public static void ValidatePrintingDefaultsUserForm(string printerQueueName, GainschaLabelPreset preset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);

        var def = GainschaLabelPresetCatalog.GetDefinition(preset);

        if (GainschaUserFormRegistryWriter.TryReadBinaryValue(
                printerQueueName,
                "User Form: Data",
                out var driverDataBytes) &&
            GainschaLabelUserFormBinary.TryFindUserFormDimensionsMm(driverDataBytes, def.WidthMm, def.HeightMm))
        {
            return;
        }

        if (GainschaPrinterDriverDataProbe.TryFindUserFormDimensionsMm(
                printerQueueName,
                def.WidthMm,
                def.HeightMm))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Padrões de Impressão incorretos: esperado USER {def.WidthMm} x {def.HeightMm} mm.");
    }
}
