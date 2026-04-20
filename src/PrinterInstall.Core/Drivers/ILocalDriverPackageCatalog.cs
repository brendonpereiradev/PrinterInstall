using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public interface ILocalDriverPackageCatalog
{
    LocalDriverPackage? TryGet(PrinterBrand brand);
}
