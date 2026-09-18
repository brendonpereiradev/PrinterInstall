using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Drivers;

public static partial class PnputilOutputParser
{
    private static readonly string[] HeaderLines =
    {
        "Microsoft PnP Utility",
        "Utilitário PnP da Microsoft",
        "Utilitario PnP da Microsoft"
    };

    [GeneratedRegex(
        @"(?i)(Failed to add|Falha ao adicionar|Access is denied|Acesso negado)",
        RegexOptions.CultureInvariant)]
    private static partial Regex FailurePattern();

    [GeneratedRegex(@"\boem\d+\.inf\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedInfPattern();

    public static string ExtractLastUsefulLine(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return string.Empty;

        var lines = log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed.Trim();
        }
        return string.Empty;
    }

    public static bool LooksSuccessful(string? output, int exitCode)
    {
        // O contador pode ser zero quando o pacote já está no Driver Store.
        // 3010 indica sucesso com reinicialização pendente; não reiniciamos o alvo.
        // 259 descreve a atualização dos dispositivos, não a ausência do pacote.
        // Só prosseguimos nesse caso se houver um INF publicado; o spooler ainda deve confirmar o registro.
        var publishedPackage = !string.IsNullOrWhiteSpace(output) && PublishedInfPattern().IsMatch(output);
        return (exitCode == 0 || exitCode == 3010 || (exitCode == 259 && publishedPackage))
            && (string.IsNullOrWhiteSpace(output) || !FailurePattern().IsMatch(output));
    }

    /// <summary>
    /// Preserva o diagnóstico completo sem deixar o contador final ocultar a causa.
    /// </summary>
    public static string ExtractFailureDetail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !IsHeaderLine(line))
            .ToArray();
        var errors = lines.Where(line => FailurePattern().IsMatch(line)).ToArray();
        return string.Join(" | ", errors.Length > 0 ? errors : lines);
    }

    public static bool IsHeaderOnly(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        return IsHeaderLine(line.Trim());
    }

    private static bool IsHeaderLine(string line)
    {
        foreach (var header in HeaderLines)
        {
            if (line.Equals(header, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
