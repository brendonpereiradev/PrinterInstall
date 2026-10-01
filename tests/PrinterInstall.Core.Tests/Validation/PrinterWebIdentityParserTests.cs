using PrinterInstall.Core.Models;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class PrinterWebIdentityParserTests
{
    [Theory]
    [InlineData("<title>M1180 Series</title><h1><img alt='EPSON'><span>M1180 Series</span></h1>", PrinterBrand.Epson, "M1180")]
    [InlineData("<meta content='SEIKO EPSON' name='author'><title>M1180 Series</title>", PrinterBrand.Epson, "M1180")]
    [InlineData("<title>EPSON&#32;M1180 Series</title>", PrinterBrand.Epson, "M1180")]
    [InlineData("<title>Lexmark CX532adwe</title>", PrinterBrand.Lexmark, "CX532ADWE")]
    [InlineData("<header><img alt='Brother'><b>HL-L5212DW</b></header>", PrinterBrand.Brother, "HL-L5212DW")]
    [InlineData("<h1>Gainscha</h1><table><tr><td>Modelo:</td><td>GA-2408T</td></tr></table>", PrinterBrand.Gainscha, "GA-2408T")]
    [InlineData("<div class='container'><span id='manufacturer'>Lexmark</span><span id='model_name'>CX532adwe</span></div>", PrinterBrand.Lexmark, "CX532ADWE")]
    [InlineData("<meta name='manufacturer' content='EPSON'><meta name='model' content='WF-C5790'>", PrinterBrand.Epson, "WF-C5790")]
    public void Parse_UsesPrinterIdentityFromTitleAndHeader(string html, PrinterBrand brand, string model)
    {
        var identity = PrinterWebIdentityParser.Parse("192.0.2.10", html);

        Assert.Equal(brand, identity.Brand);
        Assert.Equal(model, identity.Model);
        Assert.True(identity.IsIdentified);
    }

    [Theory]
    [InlineData("<title>Printer Help</title><body>Compatible with EPSON M1180 Series</body>")]
    [InlineData("<script>var example = '<title>EPSON M1180</title>';</script>")]
    [InlineData("<!-- <title>EPSON M1180</title> -->")]
    [InlineData("<title>M1180 Series</title>")]
    [InlineData("<h1><img alt='EPSON'>Web Config</h1>")]
    [InlineData("<title>Printer Help</title><table><tr><td>Supported Driver:</td><td>EPSON M1180</td></tr></table>")]
    [InlineData("<h1>EPSON</h1><table><tr><td>Printer Name:</td><td>M1180</td></tr></table>")]
    public void Parse_RequiresManufacturerAndModelInIdentityFields(string html)
    {
        var identity = PrinterWebIdentityParser.Parse("192.0.2.10", html);

        Assert.False(identity.IsIdentified);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
    }

    [Fact]
    public void Parse_ConflictingTitleAndHeader_IsNotApproved()
    {
        var identity = PrinterWebIdentityParser.Parse("192.0.2.10",
            "<title>EPSON M1180</title><h1>Lexmark CX532adwe</h1>");

        Assert.True(identity.ConflictingEvidence);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
    }

    [Fact]
    public void Parse_UnsupportedModelInExplicitField_RemainsBlocked()
    {
        var identity = PrinterWebIdentityParser.Parse("192.0.2.10",
            "<h1>EPSON Web Config</h1><table><tr><th>Model:</th><td>ET-4950</td></tr></table>");

        Assert.Equal("ET4950", identity.Model);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
    }
}
