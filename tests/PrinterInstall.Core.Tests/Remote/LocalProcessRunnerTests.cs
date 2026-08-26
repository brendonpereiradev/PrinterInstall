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

    [Fact]
    public async Task RunWithOutputAsync_HandlesTimeout_WhenProcessExceedsDuration()
    {
        // Executa comando que excede o tempo limite especificado
        var output = await LocalProcessRunner.RunWithOutputAsync(
            "powershell.exe -Command \"Start-Sleep -Milliseconds 3000\"",
            TimeSpan.FromMilliseconds(150),
            CancellationToken.None);

        Assert.True(output.Result.TimedOut);
        Assert.Equal(0u, output.Result.ReturnValue);
        Assert.Contains("Timed out", output.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunWithOutputAsync_ThrowsOperationCanceledException_WhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Deve lançar OperationCanceledException quando cancelado antes do término
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await LocalProcessRunner.RunWithOutputAsync(
                "powershell.exe -Command \"Start-Sleep -Milliseconds 3000\"",
                TimeSpan.FromSeconds(10),
                cts.Token);
        });
    }

    [Fact]
    public async Task RunAsync_ReturnsRemoteProcessResult()
    {
        var result = await LocalProcessRunner.RunAsync(
            "echo RunAsyncTest",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0u, result.ReturnValue);
    }
}
