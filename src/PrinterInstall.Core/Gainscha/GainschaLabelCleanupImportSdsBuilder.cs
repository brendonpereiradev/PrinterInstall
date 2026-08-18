using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public static class GainschaLabelCleanupImportSdsBuilder
{
    public const string CleanupFileName = "gainscha-cleanup.sds";

    public static string Build(GainschaLabelPreset preset)
    {
        var template = GainschaLabelTemplateLoader.LoadText(preset);
        GainschaLabelSdsValidator.ValidateEmbeddedTemplate(template, preset);

        var stockName = GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName;
        if (!GainschaLabelSdsStockReplacer.TryExtractStockBlock(template, stockName, out var stockBlock))
        {
            throw new InvalidOperationException(
                $"Template embutido de {preset} nao contem stock '{stockName}'.");
        }

        return GainschaLabelSdsStockReplacer.BuildSingleStockImportSds(stockBlock, template);
    }
}
