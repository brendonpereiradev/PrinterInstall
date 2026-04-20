using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class SmbRemoteDriverFileStager : IRemoteDriverFileStager
{
    public Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var paths = RemoteDriverStagingPaths.Create(host);
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            Directory.CreateDirectory(paths.UncRoot);
            CopyDirectory(localPackageFolder, paths.UncRoot, cancellationToken);
            return paths;
        }, cancellationToken);
    }

    public Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            var logPath = paths.UncLogPath(logName);
            return File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
        }, cancellationToken);
    }

    public Task WriteTextFileAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string fileName, string content, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            var target = Path.Combine(paths.UncRoot, fileName);
            Directory.CreateDirectory(paths.UncRoot);
            // Write as UTF-8 with BOM so powershell.exe parses it reliably when
            // invoked via -File on non-en-US Windows.
            File.WriteAllText(target, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }, cancellationToken);
    }

    public Task CleanupAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
                if (Directory.Exists(paths.UncRoot))
                    Directory.Delete(paths.UncRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }, CancellationToken.None);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
