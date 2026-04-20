using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed class NullLocalDriverPackageCatalog : ILocalDriverPackageCatalog
{
    public LocalDriverPackage? TryGet(PrinterBrand brand) => null;
}
