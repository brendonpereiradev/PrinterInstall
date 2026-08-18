using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelPresetCatalogTests
{
    [Theory]
    [InlineData(GainschaLabelPreset.Pulseira, 25, 270, "USER (25,0 mm x 270,0 mm)")]
    [InlineData(GainschaLabelPreset.Matrix, 50, 30, "USER (50,0 mm x 30,0 mm)")]
    [InlineData(GainschaLabelPreset.Paciente, 89, 36, "USER (89,0 mm x 36,0 mm)")]
    public void GetDefinition_ReturnsExpectedDimensionsAndDisplayName(
        GainschaLabelPreset preset, int widthMm, int heightMm, string displayName)
    {
        var def = GainschaLabelPresetCatalog.GetDefinition(preset);
        Assert.Equal(widthMm, def.WidthMm);
        Assert.Equal(heightMm, def.HeightMm);
        Assert.Equal(displayName, def.DriverStockDisplayName);
    }

    [Fact]
    public void AllPresets_AreDistinct()
    {
        var names = Enum.GetValues<GainschaLabelPreset>()
            .Select(GainschaLabelPresetCatalog.GetDefinition)
            .Select(d => d.DriverStockDisplayName)
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
