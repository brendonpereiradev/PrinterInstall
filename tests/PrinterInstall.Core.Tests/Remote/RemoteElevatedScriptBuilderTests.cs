using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemoteElevatedScriptBuilderTests
{
    [Fact]
    public void BuildCreateTcpPortScript_EmitsResultMarker()
    {
        var script = RemoteElevatedScriptBuilder.BuildCreateTcpPortScript(
            "IP_10.0.0.5", "10.0.0.5", 9100, "RAW");
        Assert.Contains("Add-PrinterPort", script, StringComparison.Ordinal);
        Assert.Contains("RESULT>> OK", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAddPrinterScript_EscapesQuotes()
    {
        var script = RemoteElevatedScriptBuilder.BuildAddPrinterScript(
            "Recepção L'Impressora", "Lexmark Universal v4 XL", "IP_10.0.0.5");
        Assert.Contains("Recepção L''Impressora", script, StringComparison.Ordinal);
        Assert.Contains("Add-Printer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapWithResultHandling_IncludesTryCatch()
    {
        var inner = "Write-Output 'hello'";
        var wrapped = RemoteElevatedScriptBuilder.WrapWithResultHandling(inner);
        Assert.Contains("$ErrorActionPreference = 'Stop'", wrapped, StringComparison.Ordinal);
        Assert.Contains("RESULT>> FAIL", wrapped, StringComparison.Ordinal);
        Assert.Contains("RESULT>> OK", wrapped, StringComparison.Ordinal);
    }
}
