using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class WmiPrinterOperationsCoreTests
{
    [Fact]
    public void BuildPrintTestPageCommandLine_EscapesSingleQuotesInPrinterName()
    {
        var cmd = WmiPrinterOperationsCore.BuildPrintTestPageCommandLine("Recepção L'Andar");

        Assert.Contains("Recepção L''Andar", cmd);
        Assert.Contains("Print-TestPage", cmd);
    }

    [Fact]
    public void DescribeInstallScriptFailure_PrefersResultLine()
    {
        const string log = """
            PNPUTIL>> some output
            RESULT>> FAIL driver not registered
            """;

        var detail = WmiPrinterOperationsCore.DescribeInstallScriptFailure(log, 1);

        Assert.Equal("driver not registered", detail);
    }

    [Fact]
    public void DescribeInstallScriptFailure_WmiStartFailure_MentionsAdministrator()
    {
        var detail = WmiPrinterOperationsCore.DescribeInstallScriptFailure("", 5, 5);

        Assert.Contains("administrator", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildInstallerScript_CapturesPnputilExitCodeBeforeOutString()
    {
        var script = WmiPrinterOperationsCore.BuildInstallerScript(
            @"C:\Temp\pkg\LMUX1l50.inf",
            "Lexmark Universal v4 XL",
            @"C:\Temp\pkg\install.log");

        Assert.Contains("$pnpOutput = & pnputil.exe /add-driver $inf /install 2>&1", script, StringComparison.Ordinal);
        Assert.Contains("$pnpExit = $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains("$pnpOutputText = ($pnpOutput | Out-String).Trim()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("| Out-String\n        $pnpExit", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLocalElevatedScript_RelaunchesElevatedWhenNotAdministrator()
    {
        var script = WmiPrinterOperationsCore.BuildLocalElevatedScript(
            @"C:\Windows\Temp\PrinterInstall\test\apply-label.log",
            "Write-Output 'RESULT>> OK'");

        Assert.Contains("-Verb RunAs", script, StringComparison.Ordinal);
        Assert.Contains("RESULT>> OK", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_RelaunchesElevatedWhenNotAdministrator()
    {
        var script = WmiPrinterOperationsCore.BuildInstallerScript(
            @"C:\Temp\pkg\LMUX1l50.inf",
            "Lexmark Universal v4 XL",
            @"C:\Temp\pkg\install.log");

        Assert.Contains("-Verb RunAs", script, StringComparison.Ordinal);
        Assert.Contains("$MyInvocation.MyCommand.Path", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_SkipRunAsBlock_OmitsElevationRelaunch()
    {
        var script = WmiPrinterOperationsCore.BuildInstallerScript(
            @"C:\Temp\pkg\LMUX1l50.inf",
            "Lexmark Universal v4 XL",
            @"C:\Temp\pkg\install.log",
            skipRunAsBlock: true);

        Assert.DoesNotContain("-Verb RunAs", script, StringComparison.Ordinal);
        Assert.Contains("$pnpOutput = & pnputil.exe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeInstallScriptFailure_PnputilHeaderOnly_UsesPnputilBlockFromLog()
    {
        const string log = """
            PNPUTIL>> Utilitário PnP da Microsoft

            Adicionando pacote de driver:  LMUX1l50.inf
            Falha ao adicionar pacote de driver: Acesso negado.
            RESULT>> FAIL pnputil: Utilitário PnP da Microsoft
            """;

        var detail = WmiPrinterOperationsCore.DescribeInstallScriptFailure(log, 1);

        Assert.Equal("pnputil: Falha ao adicionar pacote de driver: Acesso negado.", detail);
    }

    [Fact]
    public void BuildInstallerScript_TriesNameOnlyBeforeDriverStoreInfPath()
    {
        var script = WmiPrinterOperationsCore.BuildInstallerScript(
            @"C:\Temp\pkg\LMUX1l50.inf",
            "Lexmark Universal v4 XL",
            @"C:\Temp\pkg\install.log");

        Assert.Contains("SPOOLER>> Trying Add-PrinterDriver -Name", script, StringComparison.Ordinal);
        Assert.Contains("Add-PrinterDriver -Name $driverName -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$candidates.Add($inf)", script, StringComparison.Ordinal);
        Assert.Contains("RESULT>> FAIL pnputil:", script, StringComparison.Ordinal);
        Assert.Contains("printui.dll,PrintUIEntry", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeInstallScriptFailure_PnputilPrefix_ReturnsDetailAfterPrefix()
    {
        const string log = """
            PNPUTIL>> Access is denied.
            RESULT>> FAIL pnputil: Access is denied.
            """;

        var detail = WmiPrinterOperationsCore.DescribeInstallScriptFailure(log, 1);

        Assert.Equal("pnputil: Access is denied.", detail);
    }
}
