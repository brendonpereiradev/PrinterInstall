using System.Buffers.Binary;
using System.Net;
using System.Text;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Network;

public class NetworkPrinterIdentityServiceTests
{
    public static IEnumerable<object[]> SupportedModels =>
    [
        [PrinterBrand.Epson, "M1180"],
        [PrinterBrand.Epson, "WF-M5899"],
        [PrinterBrand.Epson, "WF-M5799"],
        [PrinterBrand.Epson, "WF-C5890"],
        [PrinterBrand.Epson, "WF-C5790"],
        [PrinterBrand.Lexmark, "CX532ADWE"],
        [PrinterBrand.Brother, "HL-L5212DW"],
        [PrinterBrand.Gainscha, "GA-2408T"]
    ];

    // Estrutura retornada pelo Web Config da M1180: modelo no título, marca no logotipo.
    private const string EpsonLandingPage = """
        <html><head><meta name="Author" content="SEIKO EPSON">
        <script>document.write("<meta http-equiv='refresh' content='0; URL=./PRESENTATION/HTML/TOP/INDEX.HTML'>");</script>
        </head><body><noscript>Enable your browser's JavaScript setting.</noscript></body></html>
        """;
    private const string EpsonModelPage = """
        <html><head><meta name="Author" content="SEIKO EPSON"><title>M1180 Series</title></head>
        <body><h1 class="font-size-14em"><img class="logo" src="../../IMAGE/EPSONLOGO.PNG" alt="EPSON">
        <span class="header">M1180 Series</span></h1><p>Epson Connect Services</p></body></html>
        """;
    private const string GainschaControlPage = """
        <title>Printer Control</title>
        <a href="jb_status_look.shtml">Printer Info</a>
        <a href="jb_parameter_setting.shtml">Printer Setup</a>
        <script>function print_self_test_page() { loadXMLDoc('/print_self_test_page?'); }</script>
        """;
    private const string GainschaInfoPage = """
        <title>Printer Info</title><script>
        function update() { loadXMLDoc('/updata_message?', function() {
        var text = xmlhttp.responseText.split(';');
        document.getElementById('print_model').innerHTML = text[0]; }); }
        </script><table><tr><td>Printer Model:</td><td><span id='print_model'>#</td></tr></table>
        """;
    private const string GainschaInfoResponse = "GA-2408T  ;SERIAL-TEST;G1.1.3;G2.0.1;203;0;0;0;Ready;";

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> GetRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Get)
                GetRequests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private static HttpResponseMessage GainschaPage(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/" => Html(GainschaControlPage),
        "/jb_status_look.shtml" => Html(GainschaInfoPage),
        "/updata_message" => new(HttpStatusCode.OK) { Content = new StringContent(GainschaInfoResponse + new string('\0', 512)) },
        _ => new(HttpStatusCode.NotFound)
    };

    [Theory]
    [InlineData(PrinterBrand.Gainscha, true)]
    [InlineData(PrinterBrand.Epson, false)]
    [InlineData(PrinterBrand.Lexmark, false)]
    [InlineData(PrinterBrand.Brother, false)]
    public async Task IdentifyAsync_GainschaDynamicInfo_AcceptsOnlyCorrectSelectionWithoutRunningPrinterActions(PrinterBrand selected, bool compatible)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound) : GainschaPage(request));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal(PrinterBrand.Gainscha, identity.Brand);
        Assert.Equal("GA-2408T", identity.Model);
        Assert.Equal("HTTP Gainscha Printer Info", identity.Source);
        Assert.Equal(compatible, PrinterCompatibilityValidator.Check(identity, selected).Compatible);
        Assert.Equal(new[] { "/", "/jb_status_look.shtml", "/updata_message" }, handler.GetRequests.Select(uri => uri.AbsolutePath));
    }

    [Fact]
    public async Task IdentifyAsync_GainschaInfoConflictsWithIppBrand_RemainsBlocked()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(IppModelResponse("EPSON")) }
            : GainschaPage(request));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.True(identity.ConflictingEvidence);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Gainscha).Compatible);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.OK, "GA-2408T")]
    public async Task IdentifyAsync_GainschaInfoCannotBeRead_DoesNotApproveFromTheWebMenu(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.AbsolutePath == "/updata_message"
                ? new(status) { Content = new StringContent(body) }
                : GainschaPage(request));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Gainscha).Compatible);
    }

    [Fact]
    public async Task IdentifyAsync_GenericLoginPage_DoesNotAssumeGainscha()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound) : Html("<title>Login</title><form>Printer Login</form>"));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.DoesNotContain(handler.GetRequests, uri => uri.AbsolutePath == "/updata_message");
    }

    [Theory]
    [InlineData(PrinterBrand.Epson, true)]
    [InlineData(PrinterBrand.Lexmark, false)]
    public async Task IdentifyAsync_WhenIppIsUnavailable_ReadsLegacyEpsonWebConfigAndChecksSelection(PrinterBrand selected, bool compatible)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : Html(request.RequestUri!.AbsolutePath == "/" ? EpsonLandingPage : EpsonModelPage));
        using var client = new HttpClient(handler);
        var service = new NetworkPrinterIdentityService("", client);

        var identity = await service.IdentifyAsync("192.0.2.10");

        Assert.True(identity.IsIdentified);
        Assert.Equal(PrinterBrand.Epson, identity.Brand);
        Assert.Equal("M1180", identity.Model);
        Assert.Equal("HTTP Web Config", identity.Source);
        Assert.Equal(compatible, PrinterCompatibilityValidator.Check(identity, selected).Compatible);
        Assert.Equal(new[] { "http://192.0.2.10/", "http://192.0.2.10/PRESENTATION/HTML/TOP/INDEX.HTML" },
            handler.GetRequests.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task IdentifyAsync_ConflictingIppAndWebBrands_RemainsBlocked()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(IppModelResponse("Lexmark")) }
            : Html(EpsonModelPage));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.True(identity.ConflictingEvidence);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
        Assert.Contains("fabricante", identity.Detail);
    }

    [Fact]
    public async Task IdentifyAsync_UnavailableProtocolsAndWeb_RemainsBlocked()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(HttpStatusCode.NotFound)));

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.Equal("SNMP/IPP/HTTP", identity.Source);
        Assert.False(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
    }

    [Fact]
    public async Task IdentifyAsync_WebRedirectToDifferentHost_IsNotUsedAsPrinterIdentity()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://192.0.2.99/") } });
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.All(handler.GetRequests, uri => Assert.Equal("192.0.2.10", uri.Host));
    }

    [Fact]
    public async Task IdentifyAsync_WebRedirectWithinPrinter_IsFollowed()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.AbsolutePath == "/"
                ? new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/webconfig", UriKind.Relative) } }
                : Html(EpsonModelPage));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal("M1180", identity.Model);
        Assert.Contains(handler.GetRequests, uri => uri.AbsolutePath == "/webconfig");
    }

    [Fact]
    public async Task IdentifyAsync_OversizedWebPage_IsNotUsedAsPrinterIdentity()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : Html(EpsonModelPage + new string(' ', 128 * 1024)));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
    }

    [Theory]
    [MemberData(nameof(SupportedModels))]
    public async Task IdentifyAsync_AllSupportedModels_RecognizesHttpsPageAndRejectsOtherBrands(PrinterBrand brand, string model)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.Scheme == "http"
                ? new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://192.0.2.10/") } }
                : Html($"<title>{brand} {model} Series</title>"));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal(brand, identity.Brand);
        Assert.Equal(model, identity.Model);
        Assert.Equal("HTTPS Web Config", identity.Source);
        foreach (var selected in Enum.GetValues<PrinterBrand>())
            Assert.Equal(selected == brand, PrinterCompatibilityValidator.Check(identity, selected).Compatible);
    }

    [Theory]
    [MemberData(nameof(SupportedModels))]
    public async Task IdentifyAsync_AllSupportedModels_ReadsModelFieldBehindMetaRedirect(PrinterBrand brand, string model)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.AbsolutePath == "/"
                ? Html("<meta http-equiv='refresh' content='0; URL=/status.html'>")
                : Html($"<h1>{brand} Web Control</h1><table><tr><th>Model Name:</th><td>{model}</td></tr></table>"));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal(model, identity.Model);
        Assert.True(PrinterCompatibilityValidator.Check(identity, brand).Compatible);
    }

    [Theory]
    [InlineData("M1180")]
    [InlineData("WF-M5899")]
    [InlineData("WF-M5799")]
    [InlineData("WF-C5890")]
    [InlineData("WF-C5790")]
    public async Task IdentifyAsync_EpsonAdvancedWebConfig_FollowsHttpsAndLiteralRedirect(string model)
    {
        const string advancedPath = "/PRESENTATION/ADVANCED/COMMON/TOP";
        var landing = """
            <meta name="Author" content="SEIKO EPSON"><script>
            document.write("<meta http-equiv='refresh' content='0; URL=./PRESENTATION/ADVANCED/COMMON/TOP'>");
            </script>
            """;
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.Scheme == "http"
                ? new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://192.0.2.10/") } }
                : request.RequestUri.AbsolutePath == "/"
                    ? Html(landing)
                    : request.RequestUri.AbsolutePath == advancedPath
                        ? Html($"<meta name='Author' content='SEIKO EPSON'><title>{model} Series</title><header><div id='logo'><img src='EPSONLOGO_NEW.PNG'></div><h1 id='model_name'>{model} Series</h1></header>")
                        : new(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal(model, identity.Model);
        Assert.True(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Epson).Compatible);
        Assert.Contains(handler.GetRequests, uri => uri.Scheme == "https" && uri.AbsolutePath == advancedPath);
    }

    [Fact]
    public async Task IdentifyAsync_Frameset_ReadsBrotherIdentityInStatusFrame()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : request.RequestUri!.AbsolutePath == "/"
                ? Html("<frameset><frame src='/general/status.html'></frameset>")
                : Html("<header><img alt='Brother'></header><span id='model_name'>HL-L5212DW</span>"));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.Equal("HL-L5212DW", identity.Model);
        Assert.True(PrinterCompatibilityValidator.Check(identity, PrinterBrand.Brother).Compatible);
    }

    [Theory]
    [InlineData("<meta http-equiv='refresh' content='0; URL=http://192.0.2.99/status.html'>")]
    [InlineData("<iframe src='http://192.0.2.99/status.html'></iframe>")]
    [InlineData("<script>location.href='http://192.0.2.99/status.html';</script>")]
    [InlineData("<script>location.replace('javascript:alert(1)');</script>")]
    public async Task IdentifyAsync_WebNavigation_RemainsOnRequestedPrinter(string html)
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound) : Html(html));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.All(handler.GetRequests, uri => Assert.Equal("192.0.2.10", uri.Host));
        Assert.Equal(2, handler.GetRequests.Count);
    }

    [Fact]
    public async Task IdentifyAsync_CyclicWebRedirects_StopWithoutApprovingUnknownModel()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? new(HttpStatusCode.NotFound)
            : Html($"<meta http-equiv='refresh' content='0; URL={(request.RequestUri!.AbsolutePath == "/" ? "/status.html" : "/")}'>"));
        using var client = new HttpClient(handler);

        var identity = await new NetworkPrinterIdentityService("", client).IdentifyAsync("192.0.2.10");

        Assert.False(identity.IsIdentified);
        Assert.Equal(4, handler.GetRequests.Count);
    }

    [Fact]
    public void BuildIppRequest_RequestsPrinterMakeAndModel()
    {
        var request = NetworkPrinterIdentityService.BuildIppRequest("http://192.0.2.10:631/ipp/print");
        Assert.Equal(0x0B, request[3]);
        Assert.Contains("printer-make-and-model", Encoding.UTF8.GetString(request));
        Assert.Contains("ipp://192.0.2.10:631/ipp/print", Encoding.UTF8.GetString(request));
    }

    [Fact]
    public void ReadIppMakeAndModel_ReadsAttributeAndRejectsTruncatedResponse()
    {
        using var stream = new MemoryStream();
        stream.Write([2, 0, 0, 0, 0, 0, 0, 1, 4, 0x41]);
        WriteText(stream, "printer-make-and-model");
        WriteText(stream, "Lexmark CX532adwe");
        stream.WriteByte(3);
        var packet = stream.ToArray();

        Assert.Equal("Lexmark CX532adwe", NetworkPrinterIdentityService.ReadIppMakeAndModel(packet));
        Assert.Null(NetworkPrinterIdentityService.ReadIppMakeAndModel(packet.AsSpan(0, packet.Length - 4)));
    }

    [Fact]
    public void ReadIppMakeAndModel_ReadsTextWithLanguage()
    {
        using var stream = new MemoryStream();
        stream.Write([1, 1, 0, 0, 0, 0, 0, 1, 4, 0x35]);
        WriteText(stream, "printer-make-and-model");
        using var value = new MemoryStream();
        WriteText(value, "en");
        WriteText(value, "Brother HL-L5212DW");
        var bytes = value.ToArray();
        Span<byte> size = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(size, checked((ushort)bytes.Length));
        stream.Write(size);
        stream.Write(bytes);
        stream.WriteByte(3);

        Assert.Equal("Brother HL-L5212DW", NetworkPrinterIdentityService.ReadIppMakeAndModel(stream.ToArray()));
    }

    private static void WriteText(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> size = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(size, checked((ushort)bytes.Length));
        stream.Write(size);
        stream.Write(bytes);
    }

    private static byte[] IppModelResponse(string model)
    {
        using var stream = new MemoryStream();
        stream.Write([2, 0, 0, 0, 0, 0, 0, 1, 4, 0x41]);
        WriteText(stream, "printer-make-and-model");
        WriteText(stream, model);
        stream.WriteByte(3);
        return stream.ToArray();
    }
}
