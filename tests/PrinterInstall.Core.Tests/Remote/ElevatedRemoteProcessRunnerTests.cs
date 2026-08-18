using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class ElevatedRemoteProcessRunnerTests
{
    private static readonly NetworkCredential Cred = new("user", "pass", "DOMAIN");
    private const string Host = "remote-pc";

    [Fact]
    public async Task RunElevatedScriptAsync_CreatesRunsAndDeletesScheduledTask()
    {
        var wmi = new Mock<IRemoteWmiProcessRunner>();
        var stager = new Mock<IRemoteDriverFileStager>();
        var paths = RemoteDriverStagingPaths.Create(Host);
        var commands = new List<string>();

        wmi.Setup(x => x.RunAsync(Host, Cred, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, NetworkCredential, string, TimeSpan, CancellationToken>((_, _, cmd, _, _) => commands.Add(cmd))
            .ReturnsAsync(new RemoteProcessResult(0, 123, TimedOut: false));

        stager.Setup(x => x.WriteTextFileAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), "task.ps1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        stager.Setup(x => x.ReadLogAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), "task.result", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        stager.Setup(x => x.ReadLogAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), "task.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> OK");

        stager.Setup(x => x.CleanupAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new ElevatedRemoteProcessRunner(wmi.Object, stager.Object);
        await sut.RunElevatedScriptAsync(
            Host,
            Cred,
            RemoteElevatedScriptBuilder.WrapWithResultHandling("Write-Output 'test'"),
            TimeSpan.FromMinutes(1),
            log: null,
            CancellationToken.None);

        Assert.Contains(commands, c => c.Contains("schtasks /Create", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("/RU SYSTEM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("schtasks /Run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("schtasks /Delete", StringComparison.OrdinalIgnoreCase));
        stager.Verify(x => x.CleanupAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunElevatedScriptAsUserAsync_UsesDeployUserNotSystem()
    {
        var wmi = new Mock<IRemoteWmiProcessRunner>();
        var stager = new Mock<IRemoteDriverFileStager>();
        var commands = new List<string>();
        string? capturedScript = null;

        wmi.Setup(x => x.RunAsync(Host, Cred, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, NetworkCredential, string, TimeSpan, CancellationToken>((_, _, cmd, _, _) => commands.Add(cmd))
            .ReturnsAsync(new RemoteProcessResult(0, 123, TimedOut: false));
        stager.Setup(x => x.WriteTextFileAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.ps1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, NetworkCredential, RemoteDriverStagingPaths, string, string, CancellationToken>((_, _, _, _, script, _) => capturedScript = script)
            .Returns(Task.CompletedTask);
        stager.Setup(x => x.ReadLogAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.result", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> OK");
        stager.Setup(x => x.ReadLogAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        stager.Setup(x => x.CleanupAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new ElevatedRemoteProcessRunner(wmi.Object, stager.Object);
        await sut.RunElevatedScriptAsUserAsync(
            Host,
            Cred,
            RemoteElevatedScriptBuilder.WrapWithResultHandling("Write-Output 'test'"),
            TimeSpan.FromMinutes(1),
            log: null,
            CancellationToken.None);

        Assert.Contains(commands, c => c.Contains("/RU \"DOMAIN\\user\"", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("/RP \"pass\"", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, c => c.Contains("/RU SYSTEM", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(capturedScript);
        Assert.Contains("task.result", capturedScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set-Content -LiteralPath", capturedScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunElevatedScriptAsync_ResultFail_Throws()
    {
        var wmi = new Mock<IRemoteWmiProcessRunner>();
        var stager = new Mock<IRemoteDriverFileStager>();

        wmi.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(0, 1, TimedOut: false));
        stager.Setup(x => x.WriteTextFileAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stager.Setup(x => x.ReadLogAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.result", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> FAIL Acesso negado");
        stager.Setup(x => x.ReadLogAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        stager.Setup(x => x.CleanupAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new ElevatedRemoteProcessRunner(wmi.Object, stager.Object);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunElevatedScriptAsync(Host, Cred, "Write-Output x", TimeSpan.FromMinutes(1), null, CancellationToken.None));
        Assert.Contains("Execução elevada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acesso negado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
