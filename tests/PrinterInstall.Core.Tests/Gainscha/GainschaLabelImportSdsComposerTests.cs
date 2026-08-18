using System.Text.RegularExpressions;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelImportSdsComposerTests
{
    [Fact]
    public void ComposeForImport_InjectsUserStockFromPresetData()
    {
        var template = GainschaLabelTemplateLoader.LoadText(GainschaLabelPreset.Paciente);
        var expectedStock = GainschaLabelPresetCatalog.GetDefinition(GainschaLabelPreset.Paciente).DriverStockDisplayName;

        var composed = GainschaLabelImportSdsComposer.ComposeForImport(template, expectedStock);

        Assert.Contains($"Name={expectedStock}", composed, StringComparison.Ordinal);
        Assert.Contains("<stock>", composed, StringComparison.OrdinalIgnoreCase);

        var presetData = Regex.Match(template, @"<preset[^>]*>\s*Data=([^\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        Assert.Contains($"Data={presetData}", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeForImport_LeavesStockBasedTemplateUnchanged()
    {
        const string withStock = """
            <driver version='6.6'>
            <stock>
            Name=USER (89,0 mm x 36,0 mm)
            Data=real-export
            </stock>
            </driver>
            """;

        var composed = GainschaLabelImportSdsComposer.ComposeForImport(
            withStock,
            "USER (89,0 mm x 36,0 mm)");

        Assert.Equal(withStock, composed);
    }
}
