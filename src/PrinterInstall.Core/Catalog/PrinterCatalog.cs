using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Catalog;

public static class PrinterCatalog
{
    private static readonly IReadOnlyDictionary<PrinterBrand, string> DriverNames = new Dictionary<PrinterBrand, string>
    {
        [PrinterBrand.Epson] = "EPSON Universal Print Driver",
        [PrinterBrand.Gainscha] = "Gainscha GA-2408T",
        [PrinterBrand.Lexmark] = "Lexmark Universal v4 XL"
    };

    public static string GetExpectedDriverName(PrinterBrand brand) => DriverNames[brand];
}
