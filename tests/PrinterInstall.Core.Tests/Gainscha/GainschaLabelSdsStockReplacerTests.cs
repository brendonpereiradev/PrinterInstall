using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelSdsStockReplacerTests
{
    private const string BaselineWithDefaultsAndUser = """
        <driver version='6.6'>

        <stock>
        Name=2 x 4
        Data=abc
        </stock>

        <stock>
        Name=USER (89,0 mm x 36,0 mm)
        Data=real-user-stock
        </stock>

        <options model='Gainscha GA-2408T'>
        "User Form: Data"=hex:a8,5b,01,00,a0,8c,00,00
        </options>

        </driver>
        """;

    [Fact]
    public void TryExtractStockBlock_FindsExpectedUserStock()
    {
        var expected = GainschaLabelPresetCatalog.GetDefinition(GainschaLabelPreset.Paciente).DriverStockDisplayName;

        Assert.True(GainschaLabelSdsStockReplacer.TryExtractStockBlock(BaselineWithDefaultsAndUser, expected, out var block));

        Assert.Contains("Data=real-user-stock", block, StringComparison.Ordinal);
        Assert.Contains($"Name={expected}", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStrippedBaselineImport_RemovesDefaultsAndKeepsExpectedUserStock()
    {
        const string baseline = """
            <driver version='6.6'>

            <stock>
            Name=2 x 4
            Data=abc
            </stock>

            <stock>
            Name=4 x 6
            Data=def
            </stock>

            <options model='Gainscha GA-2408T'>
            "User Form: Data"=hex:00
            </options>

            </driver>
            """;

        var template = GainschaLabelTemplateLoader.LoadText(GainschaLabelPreset.Paciente);
        var expected = GainschaLabelPresetCatalog.GetDefinition(GainschaLabelPreset.Paciente).DriverStockDisplayName;

        var importSds = GainschaLabelSdsStockReplacer.BuildStrippedBaselineImport(baseline, template, expected);

        Assert.DoesNotContain("Name=2 x 4", importSds, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=4 x 6", importSds, StringComparison.Ordinal);
        Assert.Contains($"Name={expected}", importSds, StringComparison.Ordinal);
        Assert.Contains("User Form: Data", importSds, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveForbiddenStockBlocks_StripsDriverDefaults()
    {
        var cleaned = GainschaLabelSdsStockReplacer.RemoveForbiddenStockBlocks(BaselineWithDefaultsAndUser);

        Assert.DoesNotContain("Name=2 x 4", cleaned, StringComparison.Ordinal);
        Assert.Contains("Name=USER (89,0 mm x 36,0 mm)", cleaned, StringComparison.Ordinal);
    }
}
