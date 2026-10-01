using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Validation;

internal static partial class GainschaWebIdentityParser
{
    [GeneratedRegex("\\bid\\s*=\\s*['\"]print_model['\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex ModelField();

    [GeneratedRegex("\\bloadXMLDoc\\s*\\(\\s*['\"]/updata_message\\??['\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex InfoRequest();

    [GeneratedRegex(@"^GA[ -]?\d{4}[A-Z0-9-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex ModelName();

    internal static bool HasPrinterInfoRequest(string html) => ModelField().IsMatch(html) && InfoRequest().IsMatch(html);

    internal static PrinterIdentityResult? Parse(string host, string response, string source)
    {
        // A resposta é modelo; série; firmware; boot; resolução; contadores; estado, com preenchimento NUL.
        var fields = response.TrimEnd('\0', '\r', '\n', ' ').Split(';');
        if (fields.Length < 9)
            return null;
        var model = fields[0].Trim();
        if (!ModelName().IsMatch(model) || fields.Take(5).Any(string.IsNullOrWhiteSpace) ||
            !int.TryParse(fields[4].Trim(), out var dpi) || dpi is < 100 or > 1200)
            return null;

        // O fabricante vem do modelo retornado pelo adaptador de informações, sem usar a seleção do operador.
        return PrinterIdentityParser.Parse(host, "Gainscha " + model, source);
    }
}
