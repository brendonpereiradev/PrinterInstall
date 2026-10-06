using PrinterInstall.Core.Logging;

namespace PrinterInstall.Core.Tests.Logging;

public class DiagnosticSessionTests
{
    [Fact]
    public void SessionLog_IsIndependentOfPreviousLaunches_AndRedactsExceptions()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N")));
        var first = new DiagnosticFileLogger(root);
        first.LogInfo("only-first-session");
        var second = new DiagnosticFileLogger(root);
        second.RegisterSensitiveValue("unique-secret");
        second.LogError("request unique-secret", "native", new UnauthorizedAccessException("failure unique-secret"));
        var text = second.ReadSessionLog();
        Assert.DoesNotContain("only-first-session", text);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Contains("UnauthorizedAccessException", text);
        Assert.Contains("0x80070005", text);
        Assert.DoesNotContain("unique-secret", text);
        Assert.DoesNotContain("unique-secret", File.ReadAllText(second.CurrentLogFilePath));
        Assert.Contains("[SENHA OMITIDA]", text);
    }

    [Fact]
    public void ExportReport_EmbedsContextAndTechnicalLog_WithoutNeedingOriginalFiles()
    {
        var report = LogReportFormatter.FormatDeployReport("DOMAIN\\admin", "notebook", null, "UI event",
            diagnosticContext: "Mode LOCAL; Spooler=Running; PartOfDomain=True",
            diagnosticLogText: "Stack Trace: installer failure Win32Error: 5");
        Assert.Contains("CONTEXTO DE DIAGNÓSTICO", report);
        Assert.Contains("Mode LOCAL", report);
        Assert.Contains("Spooler=Running", report);
        Assert.Contains("LOG TÉCNICO DA SESSÃO", report);
        Assert.Contains("Stack Trace: installer failure Win32Error: 5", report);
        Assert.Contains("UI event", report);
    }

    [Fact]
    public async Task LocalCollector_RecordsRealWindowsContextWithoutChangingPrinterConfiguration()
    {
        var text = await new LocalDiagnosticCollector().CollectAsync(CancellationToken.None);
        Assert.Contains("Usuário do processo:", text);
        Assert.Contains("Token elevado:", text);
        Assert.Contains("Versão do aplicativo:", text);
        Assert.Contains("Windows e dominio", text);
        Assert.Contains("Spooler", text);
    }
}
