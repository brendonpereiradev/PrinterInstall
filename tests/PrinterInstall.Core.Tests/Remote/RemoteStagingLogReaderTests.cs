using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemoteStagingLogReaderTests
{
    [Fact]
    public void ReadText_ReturnsEmptyWhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pi-missing-{Guid.NewGuid():N}.log");
        Assert.Equal(string.Empty, RemoteStagingLogReader.ReadText(path));
    }

    [Fact]
    public void ReadText_ReadsFileWhileAnotherHandleHasWriteShare()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pi-shared-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, "line1" + Environment.NewLine + "RESULT>> OK");

        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var text = RemoteStagingLogReader.ReadText(path);

        Assert.Contains("RESULT>> OK", text, StringComparison.Ordinal);
    }
}
