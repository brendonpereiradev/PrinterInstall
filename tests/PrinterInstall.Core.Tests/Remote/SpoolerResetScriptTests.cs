using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class SpoolerResetScriptTests
{
    [Fact]
    public void BuildResetSpoolerScript_WithPurgeJobs_ContainsStopStartAndPurge()
    {
        var script = RemoteElevatedScriptBuilder.BuildResetSpoolerScript(purgeJobs: true);

        Assert.Contains("Stop-Service -Name Spooler", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-Service -Name Spooler", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRINTERS", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RESULT>> OK", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RESULT>> FAIL", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildResetSpoolerScript_WithoutPurgeJobs_ContainsStopStartWithoutPurge()
    {
        var script = RemoteElevatedScriptBuilder.BuildResetSpoolerScript(purgeJobs: false);

        Assert.Contains("Stop-Service -Name Spooler", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-Service -Name Spooler", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRINTERS", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
    }
}
