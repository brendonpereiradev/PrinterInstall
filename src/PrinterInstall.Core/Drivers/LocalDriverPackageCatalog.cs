using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed class LocalDriverPackageCatalog : ILocalDriverPackageCatalog
{
    private readonly string _baseDirectory;
    private readonly IEmbeddedDriverPackageExtractor? _extractor;

    public LocalDriverPackageCatalog()
        : this(AppContext.BaseDirectory, new EmbeddedDriverPackageExtractor())
    {
    }

    public LocalDriverPackageCatalog(string baseDirectory)
        : this(baseDirectory, null)
    {
    }

    public LocalDriverPackageCatalog(string baseDirectory, IEmbeddedDriverPackageExtractor? extractor)
    {
        _baseDirectory = baseDirectory;
        _extractor = extractor;
    }

    public LocalDriverPackage? TryGet(PrinterBrand brand)
    {
        // 1. Prioriza pasta física externa se existir (ex: Drivers/Epson ao lado do executável)
        var brandFolder = Path.Combine(_baseDirectory, "Drivers", brand.ToString());
        if (Directory.Exists(brandFolder))
        {
            var directPackage = FindPackageInFolder(brandFolder, brand);
            if (directPackage != null)
                return directPackage;
        }

        // 2. Se não existir no diretório base, busca a partir do cache de drivers embutidos
        if (_extractor != null)
        {
            var extractedRoot = _extractor.GetExtractedDriversPath();
            if (!string.IsNullOrEmpty(extractedRoot))
            {
                var embeddedBrandFolder = Path.Combine(extractedRoot, brand.ToString());
                if (Directory.Exists(embeddedBrandFolder))
                {
                    var embeddedPackage = FindPackageInFolder(embeddedBrandFolder, brand);
                    if (embeddedPackage != null)
                        return embeddedPackage;
                }
            }
        }

        return null;
    }

    private static LocalDriverPackage? FindPackageInFolder(string brandFolder, PrinterBrand brand)
    {
        var inf = Directory.EnumerateFiles(brandFolder, "*.inf", SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (inf is null)
            return null;

        return new LocalDriverPackage(
            brand,
            brandFolder,
            Path.GetFileName(inf),
            PrinterCatalog.GetExpectedDriverName(brand));
    }
}

