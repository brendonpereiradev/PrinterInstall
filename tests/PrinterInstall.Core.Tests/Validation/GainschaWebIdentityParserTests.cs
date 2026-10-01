using PrinterInstall.Core.Models;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class GainschaWebIdentityParserTests
{
    [Fact]
    public void Parse_DeviceInfoResponseWithPadding_ReadsOnlyModelAndAcceptsMatchingDriver()
    {
        var identity = GainschaWebIdentityParser.Parse("192.0.2.10",
            "GA-2408T  ;SERIAL-TEST;G1.1.3;G2.0.1;203;272;51;0;Ready;" + new string('\0', 512), "HTTP Gainscha Printer Info");

        Assert.NotNull(identity);
        Assert.Equal(PrinterBrand.Gainscha, identity.Brand);
        Assert.Equal("GA-2408T", identity.Model);
        Assert.True(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Gainscha).Compatible);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
        Assert.Null(identity.Detail);
    }

    [Theory]
    [InlineData("<html><title>Login</title></html>")]
    [InlineData("GA-2408T")]
    [InlineData("GA-2408T;SERIAL;firmware;boot;203")]
    [InlineData("GA-2408T;;firmware;boot;203;0;0;0;Ready;")]
    [InlineData("GA-2408T;SERIAL;firmware;boot;unknown;0;0;0;Ready;")]
    [InlineData("GA-2408T;SERIAL;firmware;boot;60;0;0;0;Ready;")]
    [InlineData("Epson M1180;SERIAL;firmware;boot;203;0;0;0;Ready;")]
    [InlineData("GA-2408T Epson;SERIAL;firmware;boot;203;0;0;0;Ready;")]
    public void Parse_UnexpectedOrIncompleteResponse_DoesNotIdentifyPrinter(string response)
    {
        Assert.Null(GainschaWebIdentityParser.Parse("192.0.2.10", response, "HTTP Gainscha Printer Info"));
    }

    [Fact]
    public void Parse_AnotherGainschaModel_RemainsBlockedUntilApproved()
    {
        var identity = GainschaWebIdentityParser.Parse("192.0.2.10",
            "GA-3208T;SERIAL;firmware;boot;300;0;0;0;Ready;", "HTTP Gainscha Printer Info");

        Assert.NotNull(identity);
        Assert.Equal("GA-3208T", identity.Model);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Gainscha).Compatible);
    }

    [Theory]
    [InlineData("<span id='print_model'>#</span><script>loadXMLDoc('/updata_message?', update);</script>", true)]
    [InlineData("<span id='print_model'>#</span><script>loadXMLDoc('/fw_upgrade', update);</script>", false)]
    [InlineData("<title>Login</title>", false)]
    public void HasPrinterInfoRequest_RecognizesOnlyTheModelInfoEndpoint(string html, bool expected)
    {
        Assert.Equal(expected, GainschaWebIdentityParser.HasPrinterInfoRequest(html));
    }
}
