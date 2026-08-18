using System.IO.Compression;
using System.Reflection;

namespace PrinterInstall.Core.Drivers;

/// <summary>
/// Extrai e gerencia o ciclo de vida do cache dos drivers de impressora embutidos na aplicação.
/// </summary>
public sealed class EmbeddedDriverPackageExtractor : IEmbeddedDriverPackageExtractor
{
    private const string ResourceName = "PrinterInstall.Core.Drivers.EmbeddedDrivers.zip";
    private const string ExtractionMarkerFileName = ".extracted";

    private readonly string _targetDirectory;
    private readonly Assembly _resourceAssembly;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public EmbeddedDriverPackageExtractor()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrinterInstall", "Drivers"),
            typeof(EmbeddedDriverPackageExtractor).Assembly)
    {
    }

    public EmbeddedDriverPackageExtractor(string targetDirectory, Assembly resourceAssembly)
    {
        _targetDirectory = targetDirectory;
        _resourceAssembly = resourceAssembly;
    }

    public bool IsExtracted => CheckIfMarkerValid();

    public string? GetExtractedDriversPath()
    {
        if (CheckIfMarkerValid())
        {
            return _targetDirectory;
        }

        // Extrai sincronamente caso o recurso exista
        return EnsureExtractedInternal(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<string?> EnsureExtractedAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureExtractedInternal(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> EnsureExtractedInternal(CancellationToken cancellationToken)
    {
        if (CheckIfMarkerValid())
        {
            return _targetDirectory;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CheckIfMarkerValid())
            {
                return _targetDirectory;
            }

            using var stream = _resourceAssembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            var markerContent = stream.Length.ToString();

            Directory.CreateDirectory(_targetDirectory);

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        var dirPath = Path.Combine(_targetDirectory, entry.FullName);
                        Directory.CreateDirectory(dirPath);
                        continue;
                    }

                    var destinationPath = Path.Combine(_targetDirectory, entry.FullName);
                    var parentDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

            var markerPath = Path.Combine(_targetDirectory, ExtractionMarkerFileName);
            await File.WriteAllTextAsync(markerPath, markerContent, cancellationToken).ConfigureAwait(false);

            return _targetDirectory;
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool CheckIfMarkerValid()
    {
        try
        {
            var markerPath = Path.Combine(_targetDirectory, ExtractionMarkerFileName);
            if (!File.Exists(markerPath))
            {
                return false;
            }

            using var stream = _resourceAssembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return File.Exists(markerPath);
            }

            var expectedMarker = stream.Length.ToString();
            var actualMarker = File.ReadAllText(markerPath).Trim();
            return string.Equals(expectedMarker, actualMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
