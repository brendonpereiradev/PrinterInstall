using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Validation;

/// <summary>
/// Analisa heurísticamente nomes de filas de impressão e marcas selecionadas para alertar sobre possíveis divergências
/// (ex: fila de etiquetadora configurada com driver comum Epson/Brother/Lexmark, ou fila comum com driver Gainscha).
/// </summary>
public static partial class PrinterBrandHeuristicsValidator
{
    // Palavras-chave que indicam impressora térmica de etiquetas
    private static readonly string[] LabelKeywords =
    [
        "etiq",
        "etiqueta",
        "etiquetas",
        "etiquetadora",
        "label",
        "labels",
        "termo",
        "termica",
        "thermal",
        "pulseira",
        "pulseiras",
        "gainscha",
        "zebra",
        "argox",
        "datamax",
        "elgin"
    ];

    // Palavras-chave que indicam impressoras comuns de folhas (laser / jato de tinta)
    private static readonly string[] StandardPrinterKeywords =
    [
        "laser",
        "laserjet",
        "officejet",
        "pagefeed",
        "folha",
        "folhas",
        "a4",
        "multifuncional",
        "copiadora",
        "ecotank",
        "imp",
        "impress",
        "impressora",
        "impressoras",
        "print",
        "printer",
        "printers",
        "prt"
    ];

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSeparatorPattern();

    /// <summary>
    /// Inspeciona uma lista de definições de fila e retorna todos os avisos de incompatibilidade encontrados.
    /// </summary>
    public static IReadOnlyList<string> Inspect(IEnumerable<PrinterQueueDefinition>? definitions)
    {
        if (definitions is null)
            return Array.Empty<string>();

        var warnings = new List<string>();
        foreach (var def in definitions)
        {
            if (HasSuspiciousBrandMismatch(def.DisplayName, def.Brand, out var warning) && !string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }
        }

        return warnings;
    }

    /// <summary>
    /// Verifica se há suspeita de incompatibilidade entre o nome da fila e a marca selecionada.
    /// </summary>
    public static bool HasSuspiciousBrandMismatch(string? displayName, PrinterBrand brand, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var normalized = NormalizeText(displayName);
        var tokens = TokenSeparatorPattern().Split(normalized);

        if (brand != PrinterBrand.Gainscha)
        {
            // Marca comum (Epson, Brother, Lexmark): verificar se parece ser etiquetadora
            foreach (var keyword in LabelKeywords)
            {
                if (MatchesKeyword(tokens, normalized, keyword))
                {
                    warning = $"A fila '{displayName.Trim()}' parece ser uma impressora de etiquetas (termo identificado: '{keyword}'), mas a marca selecionada é '{brand}'. Verifique se o driver correto não seria Gainscha.";
                    return true;
                }
            }
        }
        else
        {
            // Marca Gainscha: se contiver algum termo de etiqueta explícito (ex: "etiq", "pulseira"), é válida mesmo com prefixos como "imp" (ex: "IMP_ETIQ_01")
            var hasLabelKeyword = LabelKeywords.Any(keyword => MatchesKeyword(tokens, normalized, keyword));
            if (!hasLabelKeyword)
            {
                // Não possui termo de etiqueta: verificar se parece ser impressora comum de documentos / folhas / termos genéricos de impressora
                foreach (var keyword in StandardPrinterKeywords)
                {
                    if (MatchesKeyword(tokens, normalized, keyword))
                    {
                        warning = $"A fila '{displayName.Trim()}' parece ser uma impressora comum de documentos (termo identificado: '{keyword}'), mas a marca selecionada é Gainscha. Verifique se a marca selecionada está correta.";
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool MatchesKeyword(string[] tokens, string fullNormalized, string keyword)
    {
        // 1. Verifica correspondência exata em qualquer token (ex: "etiq", "a4", "laser", "imp", "prt")
        if (tokens.Any(t => string.Equals(t, keyword, StringComparison.OrdinalIgnoreCase)))
            return true;

        // 2. Para palavras com mais de 3 caracteres, verifica se algum token começa com a palavra-chave (ex: "etiquetas01", "termica_posto", "impressora01")
        if (keyword.Length >= 4 && tokens.Any(t => t.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)))
            return true;

        // 3. Caso especial para prefixos comuns curtos de 3 caracteres (ex: "etiq01", "imp01", "prt01")
        if ((keyword is "etiq" or "imp" or "prt") && tokens.Any(t => t.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static string NormalizeText(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
