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

    [Fact]
    public void ForBrand_Gainscha_ReturnsTsplPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51");
        var text = Encoding.ASCII.GetString(payload);
        Assert.NotEmpty(payload);
        Assert.StartsWith("SIZE 89 mm, 36 mm", text);
        Assert.Contains("BOX", text);
        Assert.Contains(",180,", text);
        Assert.Contains("PRINT 1,1", text);
    }

    [Fact]
    public void ForBrand_Gainscha_DiffersFromEpson()
    {
        var pcl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "10.0.0.50");
        var tspl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50");
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
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50");
        var text = Encoding.ASCII.GetString(payload);
        Assert.Contains("Host: 10.0.0.50", text);
        Assert.Contains("TEST", text);
        Assert.DoesNotContain("Conectividade OK", text);
        Assert.All(payload, b => Assert.InRange(b, (byte)0, (byte)127));
    }
}
