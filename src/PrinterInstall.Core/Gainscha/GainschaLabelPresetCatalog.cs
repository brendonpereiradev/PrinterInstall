using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public sealed record GainschaLabelPresetDefinition(
    GainschaLabelPreset Preset,
    int WidthMm,
    int HeightMm,
    string DriverStockDisplayName,
    string UiDisplayName);

public static class GainschaLabelPresetCatalog
{
    public static IReadOnlyList<GainschaLabelPreset> UiDisplayOrder { get; } =
    [
        GainschaLabelPreset.Paciente,
        GainschaLabelPreset.Matrix,
        GainschaLabelPreset.Lote,
        GainschaLabelPreset.Pulseira,
    ];

    public static IReadOnlyList<GainschaLabelPreset> NetworkTestDisplayOrder { get; } =
    [
        GainschaLabelPreset.Paciente,
        GainschaLabelPreset.Matrix,
        GainschaLabelPreset.Lote,
        GainschaLabelPreset.Pulseira,
    ];

    public static IReadOnlyList<GainschaLabelPresetDefinition> All { get; } =
    [
        Def(GainschaLabelPreset.Paciente, 89, 36, "Paciente"),
        Def(GainschaLabelPreset.Matrix, 50, 30, "Matrix"),
        Def(GainschaLabelPreset.Pulseira, 25, 270, "Pulseira"),
        Def(GainschaLabelPreset.Lote, 45, 13, "Lote"),
    ];

    public static GainschaLabelPresetDefinition GetDefinition(GainschaLabelPreset preset) =>
        All.First(d => d.Preset == preset);

    private static GainschaLabelPresetDefinition Def(
        GainschaLabelPreset preset, int w, int h, string uiName) =>
        new(preset, w, h, FormatUserStock(w, h), uiName);

    public static string FormatUserStock(int widthMm, int heightMm) =>
        $"USER ({widthMm},0 mm x {heightMm},0 mm)";
}
