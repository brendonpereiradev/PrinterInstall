using System.Text;

namespace PrinterInstall.Core.Gainscha;

internal static class SeagullSdsFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);

    public static void Write(string path, string content) =>
        File.WriteAllText(path, content, Utf8NoBom);
}
