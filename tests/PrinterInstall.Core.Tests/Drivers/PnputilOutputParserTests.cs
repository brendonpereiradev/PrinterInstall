using PrinterInstall.Core.Drivers;

namespace PrinterInstall.Core.Tests.Drivers;

public class PnputilOutputParserTests
{
    [Fact]
    public void ExtractLastUsefulLine_ReturnsLastNonEmptyLine()
    {
        var log = "Microsoft PnP Utility\r\n\r\nAdding driver package:  Gprinter.inf\r\nDriver package added successfully.\r\n\r\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("Driver package added successfully.", line);
    }

    [Fact]
    public void ExtractLastUsefulLine_ReturnsEmptyWhenBlank()
    {
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine(""));
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine(null));
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine("\r\n\r\n  \r\n"));
    }

    [Fact]
    public void ExtractLastUsefulLine_TrimsTrailingWhitespace()
    {
        var log = "Line one\r\nLine two   \r\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("Line two", line);
    }

    [Fact]
    public void ExtractLastUsefulLine_WorksWithLfOnlyNewlines()
    {
        var log = "first\nsecond\nthird\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("third", line);
    }
}
