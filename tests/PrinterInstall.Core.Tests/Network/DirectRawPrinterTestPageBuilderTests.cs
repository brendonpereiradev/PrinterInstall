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

    [Fact]
    public void ForBrand_Gainscha_ReturnsEscPosPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51");
        Assert.NotEmpty(payload);
        Assert.Equal(0x1B, payload[0]); // ESC
        Assert.Equal((byte)'@', payload[1]); // ESC @ init
    }

    [Fact]
    public void ForBrand_Gainscha_DiffersFromEpson()
    {
        var pcl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "10.0.0.50");
        var escPos = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50");
        Assert.NotEqual(pcl, escPos);
    }

    [Fact]
    public void ForBrand_IncludesHostInPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "192.168.1.99");
        var text = System.Text.Encoding.ASCII.GetString(payload);
        Assert.Contains("192.168.1.99", text);
    }
}
