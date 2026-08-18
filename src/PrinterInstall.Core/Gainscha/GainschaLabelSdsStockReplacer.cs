using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Gainscha;

internal static class GainschaLabelSdsStockReplacer
{
    internal static readonly string[] ForbiddenDefaultStocks = ["2 x 4", "4 x 4", "4 x 6"];

    private static readonly Regex StockBlockRegex = new(
        @"(?is)<stock>.*?</stock>",
        RegexOptions.Compiled);

    private static readonly Regex StockNameRegex = new(
        @"(?m)^Name=([^\r\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex OptionsBlockRegex = new(
        @"(?is)<options model='[^']+'>\s*.*?</options>",
        RegexOptions.Compiled);

    public static bool TemplateContainsStockBlock(string templateContent) =>
        templateContent.Contains("<stock>", StringComparison.OrdinalIgnoreCase);

    public static bool TryExtractStockBlock(string sdsContent, string stockName, out string stockBlock)
    {
        stockBlock = string.Empty;
        ArgumentException.ThrowIfNullOrEmpty(sdsContent);
        ArgumentException.ThrowIfNullOrEmpty(stockName);

        foreach (Match match in StockBlockRegex.Matches(sdsContent))
        {
            var block = match.Value;
            var nameMatch = StockNameRegex.Match(block);
            if (!nameMatch.Success)
                continue;

            if (!string.Equals(nameMatch.Groups[1].Value.Trim(), stockName, StringComparison.Ordinal))
                continue;

            stockBlock = block.TrimEnd();
            return true;
        }

        return false;
    }

    public static string BuildSingleStockImportSds(string stockBlock, string templateContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(stockBlock);
        ArgumentException.ThrowIfNullOrEmpty(templateContent);

        var optionsMatch = OptionsBlockRegex.Match(templateContent);
        if (!optionsMatch.Success)
        {
            throw new InvalidOperationException("Template SDS missing options section.");
        }

        return $"""
            <driver version='6.6'>

            {stockBlock.Trim()}

            {optionsMatch.Value.Trim()}

            </driver>
            """;
    }

    public static string RemoveForbiddenStockBlocks(string sdsContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(sdsContent);

        var result = sdsContent;
        foreach (var forbidden in ForbiddenDefaultStocks)
        {
            var pattern = $@"(?is)(?:\r?\n)?<stock>\s*Name={Regex.Escape(forbidden)}\s*\r?\n.*?</stock>";
            result = Regex.Replace(result, pattern, string.Empty);
        }

        return result;
    }

    /// <summary>
    /// Builds a full driver export for re-import: strips default stocks from a live export,
    /// keeps only the expected USER stock, and syncs options from the embedded template.
    /// </summary>
    public static string BuildStrippedBaselineImport(
        string baselineExport,
        string templateContent,
        string expectedStockName)
    {
        ArgumentException.ThrowIfNullOrEmpty(baselineExport);
        ArgumentException.ThrowIfNullOrEmpty(templateContent);
        ArgumentException.ThrowIfNullOrEmpty(expectedStockName);

        var result = RemoveForbiddenStockBlocks(baselineExport);

        foreach (Match match in StockBlockRegex.Matches(result).Cast<Match>().Reverse())
        {
            var nameMatch = StockNameRegex.Match(match.Value);
            if (!nameMatch.Success)
                continue;

            if (string.Equals(nameMatch.Groups[1].Value.Trim(), expectedStockName, StringComparison.Ordinal))
                continue;

            result = result.Remove(match.Index, match.Length);
        }

        if (!TryExtractStockBlock(result, expectedStockName, out var userStockBlock))
        {
            if (!TryExtractStockBlock(templateContent, expectedStockName, out userStockBlock))
            {
                throw new InvalidOperationException(
                    $"Baseline export and template are missing stock '{expectedStockName}'.");
            }

            result = InjectStockBlock(result, userStockBlock);
        }

        var optionsMatch = OptionsBlockRegex.Match(templateContent);
        if (!optionsMatch.Success)
            throw new InvalidOperationException("Template SDS missing options section.");

        result = OptionsBlockRegex.IsMatch(result)
            ? OptionsBlockRegex.Replace(result, optionsMatch.Value)
            : result.Replace("</driver>", $"{optionsMatch.Value.Trim()}{Environment.NewLine}</driver>", StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static string InjectStockBlock(string sdsContent, string stockBlock)
    {
        var driverMatch = Regex.Match(sdsContent, @"(?is)(<driver version='[^']+'>\s*)");
        if (!driverMatch.Success)
            throw new InvalidOperationException("SDS content missing driver header.");

        var insertAt = driverMatch.Index + driverMatch.Length;
        return sdsContent[..insertAt] + stockBlock.TrimEnd() + Environment.NewLine + sdsContent[insertAt..];
    }
}
