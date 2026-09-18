using PrinterInstall.Core.Drivers;

namespace PrinterInstall.Core.Tests.Drivers;

public class PnputilOutputParserTests
{
    [Theory]
    [InlineData("Pacotes de driver adicionados:  0", 0, true)]
    [InlineData("Added driver packages: 0", 0, true)]
    [InlineData("Treiberpaket erfolgreich hinzugefügt.", 0, true)]
    [InlineData("Pacotes de driver adicionados: 1", 3010, true)]
    [InlineData("Pacotes de driver adicionados: 0", 5, false)]
    [InlineData("Falha ao adicionar pacote de driver: Acesso negado.", 0, false)]
    [InlineData("Pacote de driver adicionado com Ûxito. (Jß existe no sistema)\nNome Publicado: oem206.inf\nPacotes de driver adicionados: 0", 259, true)]
    [InlineData("Published Name: OEM206.INF\nAdded driver packages: 0", 259, true)]
    [InlineData("Pacotes de driver adicionados: 0", 259, false)]
    [InlineData("", 259, false)]
    [InlineData("Falha ao adicionar pacote de driver: Acesso negado.\noem206.inf", 259, false)]
    [InlineData("Nome Publicado: oem206.inf", 5, false)]
    public void LooksSuccessful_UsesExitCodeWithoutRejectingExistingPackages(string output, int code, bool expected)
    {
        Assert.Equal(expected, PnputilOutputParser.LooksSuccessful(output, code));
    }

    [Fact]
    public void ExtractFailureDetail_PreservesErrorBeforeTrailingCounters()
    {
        const string output = "Utilitário PnP da Microsoft\nFalha ao adicionar pacote de driver: arquivo ausente.\nTotal de pacotes: 1\nPacotes de driver adicionados: 0";
        Assert.Equal("Falha ao adicionar pacote de driver: arquivo ausente.", PnputilOutputParser.ExtractFailureDetail(output));
    }

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
