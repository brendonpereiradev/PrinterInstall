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

    private static readonly IReadOnlyDictionary<PrinterBrand, IReadOnlyList<PrinterModelOption>> Models =
        new Dictionary<PrinterBrand, IReadOnlyList<PrinterModelOption>>
        {
            [PrinterBrand.Epson] = new[]
            {
                new PrinterModelOption("epson-default", "Epson (Universal)")
            },
            [PrinterBrand.Lexmark] = new[]
            {
                new PrinterModelOption("lexmark-default", "Lexmark (Universal v4 XL)")
            },
            [PrinterBrand.Gainscha] = new[]
            {
                new PrinterModelOption("gainscha-default", "Gainscha (GA-2408T)")
            }
        };

    public static string GetExpectedDriverName(PrinterBrand brand) => DriverNames[brand];

    public static IReadOnlyList<PrinterModelOption> GetModels(PrinterBrand brand) => Models[brand];
}
