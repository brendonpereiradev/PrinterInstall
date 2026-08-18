using PrinterInstall.Core.Gainscha;

namespace PrinterInstall.Core.Tests.Gainscha;

public class SeagullSdsFileWriterTests
{
    [Fact]
    public async Task WriteAsync_DoesNotEmitUtf8Bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PrinterInstall-{Guid.NewGuid():N}.sds");
        try
        {
            await SeagullSdsFileWriter.WriteAsync(path, "<driver version='6.6'>\n</driver>");

            var bytes = await File.ReadAllBytesAsync(path);

            Assert.NotEmpty(bytes);
            Assert.Equal((byte)'<', bytes[0]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
