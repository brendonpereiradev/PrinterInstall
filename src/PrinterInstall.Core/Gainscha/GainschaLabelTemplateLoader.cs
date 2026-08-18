using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public static class GainschaLabelTemplateLoader
{
    public static string LoadText(GainschaLabelPreset preset)
    {
        var resourceName = TemplateResourceName(preset);
        using var stream = typeof(GainschaLabelTemplateLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Gainscha label template not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string TemplateResourceName(GainschaLabelPreset preset) =>
        $"PrinterInstall.Core.Gainscha.Templates.{preset.ToString().ToLowerInvariant()}.sds";

    public static string TemplateFileName(GainschaLabelPreset preset) =>
        $"{preset.ToString().ToLowerInvariant()}.sds";

    public static string LoadDefaultsText(GainschaLabelPreset preset)
    {
        var resourceName = DefaultsTemplateResourceName(preset);
        using var stream = typeof(GainschaLabelTemplateLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Gainscha printing-defaults template not found: {resourceName}. " +
                "Capture with scripts/Capture-GainschaLabelPreset.ps1 -Target PrintingDefaults.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string DefaultsTemplateResourceName(GainschaLabelPreset preset) =>
        $"PrinterInstall.Core.Gainscha.Templates.{preset.ToString().ToLowerInvariant()}-defaults.sds";

    public static string DefaultsTemplateFileName(GainschaLabelPreset preset) =>
        $"{preset.ToString().ToLowerInvariant()}-defaults.sds";
}
