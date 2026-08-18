namespace PrinterInstall.Core.Remote;

internal static class RemoteStagingLogReader
{
    private const int MaxReadAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    internal static string ReadText(string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        IOException? lastIo = null;
        for (var attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (IOException ex)
            {
                lastIo = ex;
                if (attempt < MaxReadAttempts - 1)
                    Thread.Sleep(RetryDelay);
            }
        }

        throw lastIo ?? new IOException($"Nao foi possivel ler o arquivo remoto '{path}'.");
    }
}
