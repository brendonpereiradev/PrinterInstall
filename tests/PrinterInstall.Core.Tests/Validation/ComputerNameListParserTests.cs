using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class ComputerNameListParserTests
{
    [Fact]
    public void Parse_Multiline_TrimsAndSkipsEmpty()
    {
        var list = ComputerNameListParser.Parse(" pc1 \r\n\r\npc2.preventsenior.local ");
        Assert.Equal(new[] { "pc1", "pc2.preventsenior.local" }, list);
    }
}
