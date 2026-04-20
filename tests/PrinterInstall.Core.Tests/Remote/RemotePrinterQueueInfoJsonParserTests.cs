using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemotePrinterQueueInfoJsonParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse(null));
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse(""));
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse("   "));
    }

    [Fact]
    public void Parse_Array_TwoItems()
    {
        var json = """[{"Name":"A","PortName":"IP_10.0.0.1"},{"Name":"B","PortName":"COM1:"}]""";
        var list = RemotePrinterQueueInfoJsonParser.Parse(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0].Name);
        Assert.Equal("IP_10.0.0.1", list[0].PortName);
    }

    [Fact]
    public void Parse_SingleObject_OneItem()
    {
        var json = """{"Name":"Only","PortName":"X"}""";
        var list = RemotePrinterQueueInfoJsonParser.Parse(json);
        Assert.Single(list);
        Assert.Equal("Only", list[0].Name);
    }

    [Fact]
    public void NormalizeInvokerLinesToJson_SkipsLeadingNoiseLine()
    {
        var raw = RemotePrinterQueueInfoJsonParser.NormalizeInvokerLinesToJson(new[]
        {
            "VERBOSE: example",
            """[{"Name":"A","PortName":"P"}]"""
        });
        Assert.NotNull(raw);
        var list = RemotePrinterQueueInfoJsonParser.Parse(raw);
        Assert.Single(list);
        Assert.Equal("A", list[0].Name);
    }

    [Fact]
    public void NormalizeInvokerLinesToJson_TrimsBom()
    {
        var raw = RemotePrinterQueueInfoJsonParser.NormalizeInvokerLinesToJson(new[] { "\uFEFF[]" });
        Assert.Equal("[]", raw);
    }
}
