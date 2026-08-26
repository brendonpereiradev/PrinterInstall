using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelCleanupImportSdsBuilderTests
{
    [Theory]
    [InlineData(GainschaLabelPreset.Paciente, 89, 36)]
    [InlineData(GainschaLabelPreset.Matrix, 50, 30)]
    [InlineData(GainschaLabelPreset.Pulseira, 25, 270)]
    [InlineData(GainschaLabelPreset.Lote, 45, 13)]
    public void Build_ContainsUserStockAndOptions(GainschaLabelPreset preset, int widthMm, int heightMm)
    {
        var importSds = GainschaLabelCleanupImportSdsBuilder.Build(preset);
        var expectedStock = GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName;

        Assert.Contains($"Name={expectedStock}", importSds, StringComparison.Ordinal);
        Assert.Contains("<options model='Gainscha GA-2408T'>", importSds, StringComparison.Ordinal);
        Assert.Contains("User Form: Data", importSds, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=2 x 4", importSds, StringComparison.Ordinal);

        Assert.True(GainschaLabelSdsValidator.TryParseUserFormDimensionsMm(importSds, out var w, out var h));
        Assert.Equal(widthMm, w);
        Assert.Equal(heightMm, h);
    }
}
