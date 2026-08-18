using System.Globalization;
using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public static class GainschaLabelSdsValidator
{
    internal const string PlaceholderStockData =
        "WkxJQhwAAAAYAAAAeJx70MPIEBDMxPCNhQGMYQDEBABZywQL";

    private static readonly string[] ForbiddenDefaultStocks = ["2 x 4", "4 x 4", "4 x 6"];

    public static IReadOnlyList<string> ParseStockNames(string sdsContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(sdsContent);

        return Regex.Matches(sdsContent, @"<stock>\s*Name=([^\r\n]+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();
    }

    public static bool TryParseUserFormDimensionsMm(string sdsContent, out int widthMm, out int heightMm)
    {
        widthMm = 0;
        heightMm = 0;

        var match = Regex.Match(
            sdsContent,
            "\"User Form: Data\"=hex:((?:[0-9a-fA-F]{2},|[\r\n\\ \\t])+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            match = Regex.Match(
                sdsContent,
                "User Form: Data=hex:((?:[0-9a-fA-F]{2},|[\r\n\\ \\t])+)",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
            return false;

        var hexTokens = match.Groups[1].Value
            .Replace("\\", "", StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hexTokens.Length < 8)
            return false;

        try
        {
            var bytes = hexTokens.Take(8).Select(token => byte.Parse(token, NumberStyles.HexNumber)).ToArray();
            widthMm = (int)(BitConverter.ToUInt32(bytes, 0) / 1000);
            heightMm = (int)(BitConverter.ToUInt32(bytes, 4) / 1000);
            return widthMm > 0 && heightMm > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void ValidateExportedSettings(string sdsContent, GainschaLabelPreset preset)
    {
        if (TryParseUserFormDimensionsMm(sdsContent, out var widthMm, out var heightMm))
        {
            ValidateUserFormDimensions(widthMm, heightMm, preset);
            return;
        }

        var stocks = ParseStockNames(sdsContent);
        if (stocks.Count > 0)
        {
            ValidateStockList(stocks, preset);
            return;
        }

        throw new InvalidOperationException(
            "Nenhum papel de etiqueta encontrado apos importar preferencias. " +
            "Verifique se o template .sds exportado contem stocks USER ou User Form: Data.");
    }

    public static void ValidateEmbeddedTemplate(string templateContent, GainschaLabelPreset preset)
    {
        var stocks = ParseStockNames(templateContent);
        if (stocks.Count > 0)
        {
            var stockDataMatch = Regex.Match(templateContent, @"<stock>\s*Name=[^\r\n]+\s*Data=([^\r\n]+)", RegexOptions.IgnoreCase);
            if (stockDataMatch.Success &&
                string.Equals(stockDataMatch.Groups[1].Value.Trim(), PlaceholderStockData, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Template embutido de {preset} ainda usa dados placeholder do driver. " +
                    "Exporte um .sds real com ssdal apos configurar USER manualmente (veja drivers/Gainscha/label-presets/README.md).");
            }

            var expected = GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName;
            if (!stocks.Contains(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Template embutido de {preset} deve conter stock '{expected}', mas encontrado: {FormatStockList(stocks)}.");
            }

            return;
        }

        if (!TryParseUserFormDimensionsMm(templateContent, out var widthMm, out var heightMm))
        {
            throw new InvalidOperationException(
                $"Template embutido de {preset} nao contem stocks nem User Form: Data valido.");
        }

        ValidateUserFormDimensions(widthMm, heightMm, preset);
    }

    private static void ValidateStockList(IReadOnlyList<string> stocks, GainschaLabelPreset preset)
    {
        var expected = GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName;

        if (!stocks.Contains(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Esperado stock '{expected}' apos importar preferencias, mas encontrado: {FormatStockList(stocks)}.");
        }

        var forbidden = stocks
            .Where(s => ForbiddenDefaultStocks.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (forbidden.Count > 0)
        {
            throw new InvalidOperationException(
                $"Perfis padrao do driver ainda presentes apos importar preferencias: {FormatStockList(forbidden)}.");
        }

        if (stocks.Count != 1)
        {
            throw new InvalidOperationException(
                $"Esperado exatamente um papel de etiqueta apos importar preferencias, mas encontrado {stocks.Count}: {FormatStockList(stocks)}.");
        }
    }

    private static void ValidateUserFormDimensions(int widthMm, int heightMm, GainschaLabelPreset preset)
    {
        var def = GainschaLabelPresetCatalog.GetDefinition(preset);
        if (widthMm != def.WidthMm || heightMm != def.HeightMm)
        {
            throw new InvalidOperationException(
                $"Dimensao USER apos importar preferencias incorreta para {preset}: " +
                $"esperado {def.WidthMm}x{def.HeightMm} mm, encontrado {widthMm}x{heightMm} mm.");
        }
    }

    private static string FormatStockList(IEnumerable<string> stocks) =>
        string.Join(", ", stocks.Select(s => $"'{s}'"));
}
