using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelSdsValidatorTests
{
    private const string DefaultsSds = """
        <driver version='6.6'>

        <stock>
        Name=2 x 4
        Data=abc
        </stock>

        <stock>
        Name=4 x 4
        Data=def
        </stock>

        <stock>
        Name=4 x 6
        Data=ghi
        </stock>

        </driver>
        """;

    private const string PacienteOnlySds = """
        <driver version='6.6'>

        <stock>
        Name=USER (89,0 mm x 36,0 mm)
        Data=real-export
        </stock>

        </driver>
        """;

    private const string PacienteUserFormSds = """
        <options model='Gainscha GA-2408T'>
        [DATA:\Settings]
        "User Form: Data"=hex:a8,5b,01,00,a0,8c,00,00,00,00,00,00,00,00,00,00,00,00,00,\
          00,00,00,00,00,01,00,00,01
        "User Form: Name"="USER"
        </options>
        """;

    [Fact]
    public void ParseStockNames_IgnoresFontEntries()
    {
        const string sds = """
            <driver version='6.6'>
            <stock>
            Name=USER (50,0 mm x 30,0 mm)
            Data=abc
            </stock>
            <font type='Bar Code'>
            Name=Sample Bar Code Font
            Data=xyz
            </font>
            </driver>
            """;

        var names = GainschaLabelSdsValidator.ParseStockNames(sds);

        Assert.Equal(["USER (50,0 mm x 30,0 mm)"], names);
    }

    [Fact]
    public void TryParseUserFormDimensionsMm_ParsesCapturedPacienteExport()
    {
        Assert.True(GainschaLabelSdsValidator.TryParseUserFormDimensionsMm(PacienteUserFormSds, out var width, out var height));
        Assert.Equal(89, width);
        Assert.Equal(36, height);
    }

    [Fact]
    public void ValidateExportedSettings_RejectsDriverDefaults()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GainschaLabelSdsValidator.ValidateExportedSettings(DefaultsSds, GainschaLabelPreset.Paciente));

        Assert.Contains("89,0 mm x 36,0 mm", ex.Message);
    }

    [Fact]
    public void ValidateExportedSettings_AcceptsSingleExpectedStock()
    {
        GainschaLabelSdsValidator.ValidateExportedSettings(PacienteOnlySds, GainschaLabelPreset.Paciente);
    }

    [Fact]
    public void ValidateExportedSettings_AcceptsUserFormExport()
    {
        GainschaLabelSdsValidator.ValidateExportedSettings(PacienteUserFormSds, GainschaLabelPreset.Paciente);
    }

    [Fact]
    public void ValidateExportedSettings_AcceptsUserFormWhenDimensionsMatchDespiteDefaultStocks()
    {
        const string export = """
            <driver version='6.6'>
            <stock>
            Name=2 x 4
            Data=abc
            </stock>
            <options model='Gainscha GA-2408T'>
            "User Form: Data"=hex:a8,5b,01,00,a0,8c,00,00,00,00,00,00,00,00,00,00,00,00,00,\
              00,00,00,00,00,01,00,00,01
            </options>
            </driver>
            """;

        GainschaLabelSdsValidator.ValidateExportedSettings(export, GainschaLabelPreset.Paciente);
    }

    [Fact]
    public void TryParseUserFormDimensionsMm_AcceptsUnquotedExportFormat()
    {
        const string export = """
            <options model='Gainscha GA-2408T'>
            User Form: Data=hex:a8,5b,01,00,a0,8c,00,00,00,00,00,00,00,00,00,00,00,00,00,\
              00,00,00,00,00,01,00,00,01
            </options>
            """;

        Assert.True(GainschaLabelSdsValidator.TryParseUserFormDimensionsMm(export, out var width, out var height));
        Assert.Equal(89, width);
        Assert.Equal(36, height);
    }

    [Fact]
    public void ValidateEmbeddedTemplate_RejectsKnownPlaceholderStockData()
    {
        const string placeholder = """
            <stock>
            Name=USER (89,0 mm x 36,0 mm)
            Data=WkxJQhwAAAAYAAAAeJx70MPIEBDMxPCNhQGMYQDEBABZywQL
            </stock>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GainschaLabelSdsValidator.ValidateEmbeddedTemplate(placeholder, GainschaLabelPreset.Paciente));

        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryExtractStockBlock_FindsStockWithExtraLinesBeforeClosingTag()
    {
        const string sds = """
            <stock>
            Name=USER (89,0 mm x 36,0 mm)
            Data=real-user-stock
            Type=Die Cut
            </stock>
            """;

        Assert.True(
            GainschaLabelSdsStockReplacer.TryExtractStockBlock(
                sds,
                "USER (89,0 mm x 36,0 mm)",
                out var block));

        Assert.Contains("Type=Die Cut", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GainschaLabelPreset.Paciente)]
    [InlineData(GainschaLabelPreset.Matrix)]
    [InlineData(GainschaLabelPreset.Pulseira)]
    public void ValidateEmbeddedTemplate_AcceptsEmbeddedTemplates(GainschaLabelPreset preset)
    {
        var template = GainschaLabelTemplateLoader.LoadText(preset);
        GainschaLabelSdsValidator.ValidateEmbeddedTemplate(template, preset);

        var expected = GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName;
        Assert.True(GainschaLabelSdsStockReplacer.TryExtractStockBlock(template, expected, out _));
    }
}
