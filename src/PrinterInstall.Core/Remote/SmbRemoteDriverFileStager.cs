using System.IO.Compression;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class SmbRemoteDriverFileStager : IRemoteDriverFileStager
{
    private static readonly object CacheLock = new();

    public async Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken)
    {
        using var share = await SmbShareConnection.OpenAsync(host, "ADMIN$", credential, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = RemoteDriverStagingPaths.Create(host);
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
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken)
    {
        using var share = await SmbShareConnection.OpenAsync(host, "ADMIN$", credential, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var logPath = paths.UncLogPath(logName);
            return RemoteStagingLogReader.ReadText(logPath);
        }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextFileAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string fileName, string content, CancellationToken cancellationToken)
    {
        using var share = await SmbShareConnection.OpenAsync(host, "ADMIN$", credential, cancellationToken).ConfigureAwait(false);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(paths.UncRoot, fileName);
            Directory.CreateDirectory(paths.UncRoot);
            // Write as UTF-8 with BOM so powershell.exe parses it reliably when
            // invoked via -File on non-en-US Windows.
            File.WriteAllText(target, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var share = await SmbShareConnection.OpenAsync(host, "ADMIN$", credential, budget.Token).ConfigureAwait(false);
            await Task.Run(() =>
            {
                budget.Token.ThrowIfCancellationRequested();
                if (Directory.Exists(paths.UncRoot))
                    Directory.Delete(paths.UncRoot, recursive: true);
            }, budget.Token).ConfigureAwait(false);
        }
        catch { /* Limpeza não deve impedir o fim do cancelamento. */ }
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
