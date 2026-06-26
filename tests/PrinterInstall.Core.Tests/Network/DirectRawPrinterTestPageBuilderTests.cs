using System.Text;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestPageBuilderTests
{
    [Theory]
    [InlineData(PrinterBrand.Epson)]
    [InlineData(PrinterBrand.Lexmark)]
    public void ForBrand_PclBrands_ReturnsNonEmptyPayload(PrinterBrand brand)
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(brand, "10.0.0.50");
        Assert.NotEmpty(payload);
        Assert.Contains((byte)0x1B, payload); // ESC — PCL reset
    }

    [Theory]
    [InlineData(GainschaLabelPreset.Paciente, "SIZE 89 mm, 36 mm", "Paciente")]
    [InlineData(GainschaLabelPreset.Matrix, "SIZE 50 mm, 30 mm", "Matrix")]
    [InlineData(GainschaLabelPreset.Dupla, "SIZE 45 mm, 13 mm", "Dupla")]
    [InlineData(GainschaLabelPreset.Pulseira, "SIZE 25 mm, 270 mm", "Pulseira")]
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
}
