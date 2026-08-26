using System.Text;
using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestPageBuilderTests
{
    [Theory]
    [InlineData(PrinterBrand.Epson)]
    [InlineData(PrinterBrand.Lexmark)]
    [InlineData(PrinterBrand.Brother)]
    public void ForBrand_PclBrands_ReturnsNonEmptyPayload(PrinterBrand brand)
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(brand, "10.0.0.50");
        Assert.NotEmpty(payload);
        Assert.Contains((byte)0x1B, payload); // ESC — PCL reset
    }

    [Theory]
    [InlineData(GainschaLabelPreset.Paciente, "SIZE 89 mm, 36 mm", "Paciente")]
    [InlineData(GainschaLabelPreset.Matrix, "SIZE 50 mm, 30 mm", "Matrix")]
    [InlineData(GainschaLabelPreset.Lote, "SIZE 93 mm, 13 mm", "Lote")]
    public void ForBrand_Gainscha_UsesPresetDimensions(
        GainschaLabelPreset preset, string expectedSizeLine, string expectedPresetLabel)
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51", preset);
        var text = Encoding.ASCII.GetString(payload);
        Assert.NotEmpty(payload);
        Assert.StartsWith(expectedSizeLine, text);
        Assert.Contains("PRINT 1,1", text);
        Assert.Contains(expectedPresetLabel, text);
        Assert.Contains("Host: 10.0.0.51", text);
    }

    [Fact]
    public void ForBrand_Gainscha_Lote_PrintsDualColumnLayout()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(
            PrinterBrand.Gainscha, "10.0.0.51", GainschaLabelPreset.Lote);
        var text = Encoding.ASCII.GetString(payload);

        Assert.StartsWith("SIZE 93 mm, 13 mm", text);
        Assert.Contains("GAP 3 mm, 0 mm", text);

        // Ambas as colunas contêm moldura BOX
        var boxMatches = Regex.Matches(text, @"BOX (\d+),(\d+),(\d+),(\d+),\d+");
        Assert.Equal(2, boxMatches.Count);

        // Coluna 1 (Esquerda: X < 360)
        var col1BoxLeft = int.Parse(boxMatches[0].Groups[3].Value);
        var col1BoxRight = int.Parse(boxMatches[0].Groups[1].Value);
        Assert.True(col1BoxLeft >= 0 && col1BoxRight <= 360);

        // Coluna 2 (Direita: X > 380)
        var col2BoxLeft = int.Parse(boxMatches[1].Groups[3].Value);
        var col2BoxRight = int.Parse(boxMatches[1].Groups[1].Value);
        Assert.True(col2BoxLeft >= 380 && col2BoxRight <= 744);

        // Ambas as colunas contêm os textos esperados
        var testLoteMatches = Regex.Matches(text, @"TEXT (\d+),\d+,""2"",180,1,1,""TEST - Lote""");
        Assert.Equal(2, testLoteMatches.Count);

        var hostMatches = Regex.Matches(text, @"TEXT (\d+),\d+,""1"",180,1,1,""Host: 10\.0\.0\.51""");
        Assert.Equal(2, hostMatches.Count);

        Assert.Contains("PRINT 1,1", text);
    }

    [Fact]
    public void ForBrand_Gainscha_Pulseira_UsesCleanTestLayoutInHotZone()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51", GainschaLabelPreset.Pulseira);
        var text = Encoding.ASCII.GetString(payload);
        Assert.NotEmpty(payload);
        Assert.StartsWith("SIZE 25 mm, 270 mm", text);
        Assert.Contains("PRINT 1,1", text);
        Assert.Contains("Printer Install", text);
        Assert.Contains("BOX", text);
    }

    [Fact]
    public void ForBrand_Gainscha_DefaultsToPacienteWhenPresetNull()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50");
        var text = Encoding.ASCII.GetString(payload);
        Assert.StartsWith("SIZE 89 mm, 36 mm", text);
    }

    [Fact]
    public void ForBrand_Gainscha_DiffersFromEpson()
    {
        var pcl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "10.0.0.50");
        var tspl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50", GainschaLabelPreset.Paciente);
        Assert.NotEqual(pcl, tspl);
    }

    [Fact]
    public void ForBrand_IncludesHostInPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "192.168.1.99");
        var text = Encoding.ASCII.GetString(payload);
        Assert.Contains("192.168.1.99", text);
        Assert.Contains("Pagina de teste", text);
        Assert.Contains("a conectividade desta impressora esta OK", text);
        Assert.All(payload, b => Assert.InRange(b, (byte)0, (byte)127));
    }

    [Theory]
    [InlineData(GainschaLabelPreset.Matrix, "Matrix")]
    [InlineData(GainschaLabelPreset.Paciente, "Paciente")]
    public void ForBrand_Gainscha_StandardPresets_TestLabelDoesNotOverlapPresetName(
        GainschaLabelPreset preset, string presetLabel)
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51", preset);
        var text = Encoding.ASCII.GetString(payload);

        var testY = GetTextLineY(text, "TEST");
        var presetY = GetTextLineY(text, presetLabel);

        Assert.True(
            Math.Abs(testY - presetY) >= 40,
            $"Expected at least 40 dots between TEST (y={testY}) and {presetLabel} (y={presetY}).");
    }

    [Fact]
    public void ForBrand_Gainscha_IncludesHostAndTimestampWithoutConnectivityLine()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(
            PrinterBrand.Gainscha, "10.0.0.50", GainschaLabelPreset.Paciente);
        var text = Encoding.ASCII.GetString(payload);
        Assert.Contains("Host: 10.0.0.50", text);
        Assert.Contains("TEST", text);
        Assert.DoesNotContain("Conectividade OK", text);
        Assert.All(payload, b => Assert.InRange(b, (byte)0, (byte)127));
    }

    private static int GetTextLineY(string tspl, string content)
    {
        var match = Regex.Match(tspl, $@"TEXT \d+,(\d+),.*""{Regex.Escape(content)}""");
        Assert.True(match.Success, $"TEXT line containing \"{content}\" not found.");
        return int.Parse(match.Groups[1].Value);
    }
}
