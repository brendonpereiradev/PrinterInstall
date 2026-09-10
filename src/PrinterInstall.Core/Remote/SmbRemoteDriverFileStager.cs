using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class SmbRemoteDriverFileStager : IRemoteDriverFileStager
{
    private static readonly object CacheLock = new();

    public Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var paths = RemoteDriverStagingPaths.Create(host);
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            Directory.CreateDirectory(paths.UncRoot);

            try
            {
                var zipPath = GetOrCreateZippedPackage(localPackageFolder, cancellationToken);
                var targetZip = Path.Combine(paths.UncRoot, "package.zip");
                File.Copy(zipPath, targetZip, overwrite: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallback para cópia arquivo por arquivo caso ocorra qualquer problema com ZIP
                CopyDirectory(localPackageFolder, paths.UncRoot, cancellationToken);
            }

            return paths;
        }, cancellationToken);
    }

    public Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            var logPath = paths.UncLogPath(logName);
            return RemoteStagingLogReader.ReadText(logPath);
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

    private static string GetOrCreateZippedPackage(string localPackageFolder, CancellationToken cancellationToken)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "PrinterInstall", "DriverZipCache");
        Directory.CreateDirectory(cacheDir);

        var folderName = new DirectoryInfo(localPackageFolder).Name;
        var fingerprint = ComputeDirectoryFingerprint(localPackageFolder);
        var zipPath = Path.Combine(cacheDir, $"{folderName}_{fingerprint}.zip");

        if (File.Exists(zipPath))
            return zipPath;

        lock (CacheLock)
        {
            if (File.Exists(zipPath))
                return zipPath;

            cancellationToken.ThrowIfCancellationRequested();

            var tempZip = Path.Combine(cacheDir, $"{folderName}_{Guid.NewGuid():N}.tmp");
            try
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);

                ZipFile.CreateFromDirectory(localPackageFolder, tempZip, CompressionLevel.Fastest, includeBaseDirectory: false);
                File.Move(tempZip, zipPath, overwrite: true);
                return zipPath;
            }
            catch
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
                throw;
            }
        }
    }

    private static string ComputeDirectoryFingerprint(string folder)
    {
        long ticks = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                ticks += File.GetLastWriteTimeUtc(file).Ticks;
                count++;
            }
            catch
            {
                // Ignora falhas de leitura de carimbo em arquivos transitórios
            }
        }

        return $"{count}_{ticks:X}";
    }
}
