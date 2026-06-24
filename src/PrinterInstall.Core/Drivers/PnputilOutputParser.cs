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
        @"(?i)(Driver package added successfully|Pacote de driver adicionado|Added driver packages:\s*[1-9]\d*|Pacotes de driver adicionados:\s*[1-9]\d*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SuccessPattern();

    [GeneratedRegex(
        @"(?i)(Failed to add|Falha ao adicionar|Access is denied|Acesso negado|Added driver packages:\s*0|Pacotes de driver adicionados:\s*0)",
        RegexOptions.CultureInvariant)]
    private static partial Regex FailurePattern();

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
        if (string.IsNullOrWhiteSpace(output))
            return exitCode == 0;

        if (FailurePattern().IsMatch(output))
            return false;

        return exitCode == 0 && SuccessPattern().IsMatch(output);
    }

    /// <summary>
    /// Última linha útil do output do pnputil, ignorando cabeçalhos localizados.
    /// </summary>
    public static string ExtractFailureDetail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            if (IsHeaderLine(trimmed))
                continue;
            return trimmed;
        }

        return ExtractLastUsefulLine(output);
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
