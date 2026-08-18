using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class LocalProcessRunnerTests
{
    [Fact]
    public async Task RunWithOutputAsync_UsesLocalWorkingDirectory_WhenParentMayBeUnc()
    {
        var output = await LocalProcessRunner.RunWithOutputAsync(
            "echo PrinterInstallWorkingDirectoryTest",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.False(output.Result.TimedOut);
        Assert.Equal(0u, output.Result.ReturnValue);
        Assert.Contains("PrinterInstallWorkingDirectoryTest", output.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("UNC", output.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunExecutableWithOutputAsync_ExecutesQuotedPathArguments()
    {
        var output = await LocalProcessRunner.RunExecutableWithOutputAsync(
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            "/c echo PrinterInstallExecutableTest",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.False(output.Result.TimedOut);
        Assert.Equal(0u, output.Result.ReturnValue);
        Assert.Contains("PrinterInstallExecutableTest", output.StandardOutput, StringComparison.Ordinal);
    }
}
