using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Validation;

public static partial class PrinterIdentityParser
{
    [GeneratedRegex(@"\b(epson|lexmark|brother|gainscha)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandPattern();

    [GeneratedRegex(@"\b(?:WF[ -]?[MC][ -]?\d{4}|M\d{4}|ET[ -]?\d{4}|L\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpsonModelPattern();

    [GeneratedRegex(@"\b(?:CX|MX|MS|CS|MC|C|B)\d{3,4}[A-Z0-9-]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LexmarkModelPattern();

    [GeneratedRegex(@"\b(?:HL|MFC|DCP)[ -]?[A-Z]?\d{3,5}[A-Z0-9-]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrotherModelPattern();

    [GeneratedRegex(@"\bGA[ -]?\d{4}[A-Z0-9-]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GainschaModelPattern();

    public static PrinterIdentityResult Parse(string host, string description, string source)
    {
        if (string.IsNullOrWhiteSpace(description))
            return new(host, null, null, source, "Resposta sem fabricante ou modelo.");

        var brands = BrandPattern().Matches(description)
            .Select(m => Enum.Parse<PrinterBrand>(m.Value, true))
            .Distinct()
            .ToArray();
        if (brands.Length > 1)
            return new(host, null, null, source, "Resposta contém fabricantes conflitantes.", true);

        PrinterBrand? brand = brands.Length == 1 ? brands[0] : null;
        var pattern = brand switch
        {
            PrinterBrand.Epson => EpsonModelPattern(),
            PrinterBrand.Lexmark => LexmarkModelPattern(),
            PrinterBrand.Brother => BrotherModelPattern(),
            PrinterBrand.Gainscha => GainschaModelPattern(),
            _ => null
        };
        var models = pattern?.Matches(description)
            .Select(m => NormalizeModel(m.Value, brand!.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (models.Length > 1)
            return new(host, brand, null, source, "Resposta contém vários modelos possíveis.", true);

        var model = models.Length == 1 ? models[0] : null;
        return new(host, brand, model, source,
            brand is null ? "Fabricante não reconhecido na resposta." :
            model is null ? "Modelo não reconhecido na resposta." : null);
    }

    private static string NormalizeModel(string value, PrinterBrand brand)
    {
        var compact = value.ToUpperInvariant().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return brand switch
        {
            PrinterBrand.Epson when compact.StartsWith("WF", StringComparison.Ordinal) => "WF-" + compact[2..],
            PrinterBrand.Brother when compact.StartsWith("HL", StringComparison.Ordinal) => "HL-" + compact[2..],
            PrinterBrand.Brother when compact.StartsWith("MFC", StringComparison.Ordinal) => "MFC-" + compact[3..],
            PrinterBrand.Brother when compact.StartsWith("DCP", StringComparison.Ordinal) => "DCP-" + compact[3..],
            PrinterBrand.Gainscha => "GA-" + compact[2..],
            _ => compact
        };
    }
}
