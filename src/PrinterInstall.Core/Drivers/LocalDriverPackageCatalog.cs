using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed class LocalDriverPackageCatalog : ILocalDriverPackageCatalog
{
    private readonly string _baseDirectory;

    public LocalDriverPackageCatalog() : this(AppContext.BaseDirectory) { }

    public LocalDriverPackageCatalog(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public LocalDriverPackage? TryGet(PrinterBrand brand)
    {
        var brandFolder = Path.Combine(_baseDirectory, "Drivers", brand.ToString());
        if (!Directory.Exists(brandFolder))
            return null;

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
