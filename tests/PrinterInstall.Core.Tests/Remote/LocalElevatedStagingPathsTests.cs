using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class LocalElevatedStagingPathsTests
{
    [Fact]
    public void Create_UsesUserTempLikeLocalDriverInstall()
    {
        var paths = LocalElevatedStagingPaths.Create();

        try
        {
            var userTemp = Path.Combine(Path.GetTempPath(), "PrinterInstall");
            Assert.StartsWith(userTemp, paths.Root, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(paths.Root));
        }
        finally
        {
            if (Directory.Exists(paths.Root))
                Directory.Delete(paths.Root, recursive: true);
        }
    }
}
