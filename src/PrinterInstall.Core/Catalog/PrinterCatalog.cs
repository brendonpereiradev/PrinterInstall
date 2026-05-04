using System.Collections.Generic;
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

    private static readonly IReadOnlyDictionary<PrinterBrand, IReadOnlyList<string>> DriverResolutionOrder =
        new Dictionary<PrinterBrand, IReadOnlyList<string>>
        {
            [PrinterBrand.Epson] = new[] { "EPSON Universal Print Driver" },
            [PrinterBrand.Gainscha] = new[] { "Gainscha GA-2408T" },
            [PrinterBrand.Lexmark] = new[] { "Lexmark Universal v4 XL", "Lexmark Universal v2 XL" },
        };

    public static string GetExpectedDriverName(PrinterBrand brand) => DriverNames[brand];

    public static IReadOnlyList<string> GetDriverResolutionOrder(PrinterBrand brand) => DriverResolutionOrder[brand];

    public static string DescribeAcceptableDrivers(PrinterBrand brand)
    {
        var order = GetDriverResolutionOrder(brand);
        return order.Count == 1 ? order[0] : string.Join(" or ", order);
    }
}
