using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class LocalWmiProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CompletesSchtasksHelpWithoutProcessExitCodeException()
    {
        var sut = new LocalWmiProcessRunner();
        var result = await sut.RunAsync(
            "schtasks /Query /TN \"Microsoft\\Windows\\UpdateOrchestrator\\Reboot\"",
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        // schtasks returns non-zero when task is missing; either outcome proves we did not throw
        // "Process was not started by this object" while waiting for WMI-started processes.
        Assert.True(result.ReturnValue is 0 or 1);
    }
}
