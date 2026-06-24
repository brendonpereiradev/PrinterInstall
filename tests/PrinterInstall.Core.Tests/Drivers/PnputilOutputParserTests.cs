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
    public void ExtractFailureDetail_SkipsLocalizedHeader()
    {
        const string log = """
            Utilitário PnP da Microsoft

            Adicionando pacote de driver:  LMUX1l50.inf
            Falha ao adicionar pacote de driver: Acesso negado.
            """;

        var line = PnputilOutputParser.ExtractFailureDetail(log);

        Assert.Equal("Falha ao adicionar pacote de driver: Acesso negado.", line);
    }

    [Fact]
    public void LooksSuccessful_PortugueseSuccess_ReturnsTrue()
    {
        const string log = """
            Utilitário PnP da Microsoft
            Pacote de driver adicionado com êxito.
            Pacotes de driver adicionados:  1
            """;

        Assert.True(PnputilOutputParser.LooksSuccessful(log, 0));
    }

    [Fact]
    public void LooksSuccessful_AccessDenied_ReturnsFalse()
    {
        const string log = """
            Utilitário PnP da Microsoft
            Falha ao adicionar pacote de driver: Acesso negado.
            Pacotes de driver adicionados:  0
            """;

        Assert.False(PnputilOutputParser.LooksSuccessful(log, 5));
    }
}
