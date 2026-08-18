using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Validation;

/// <summary>
/// Valida endereços de rede (IPv4, IPv6, Hostname) e identifica inversões entre nome da impressora e endereço IP.
/// </summary>
public static partial class PrinterHostValidator
{
    // Padrão RFC 1123 para hostnames e FQDNs válidos (sem espaços, acentos ou pontuação inválida)
    [GeneratedRegex(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnamePattern();

    [GeneratedRegex(@"^\d+(\.\d+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllNumericDottedPattern();

    /// <summary>
    /// Verifica se o valor informado é estritamente um endereço IP (IPv4 no formato de 4 octetos ou IPv6).
    /// </summary>
    public static bool IsStrictIpAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        // Garante formato exato de IPv4 com 4 partes numéricas (0-255)
        if (IPAddress.TryParse(trimmed, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var parts = trimmed.Split('.');
                if (parts.Length == 4 && parts.All(p => byte.TryParse(p, out _)))
                    return true;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Verifica se o valor informado é um endereço de host de rede válido (IPv4, IPv6 ou hostname RFC 1123).
    /// </summary>
    public static bool IsValidHostAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var trimmed = host.Trim();

        if (trimmed.Length > 253)
            return false;

        if (IsStrictIpAddress(trimmed))
            return true;

        // Se for uma sequência de números separados por ponto (ex: 10.1.152.300), é uma tentativa inválida de IP
        if (AllNumericDottedPattern().IsMatch(trimmed))
            return false;

        // Valida se atende à especificação de hostname / FQDN (RFC 1123)
        return HostnamePattern().IsMatch(trimmed);
    }

    /// <summary>
    /// Detecta se houve provável inversão de valores entre o nome da impressora e o endereço de host.
    /// Retorna verdadeiro quando o DisplayName possui formato de IP e o HostAddress não é um IP válido.
    /// </summary>
    public static bool DetectProbableInversion(string? displayName, string? hostAddress)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(hostAddress))
            return false;

        var nameTrimmed = displayName.Trim();
        var hostTrimmed = hostAddress.Trim();

        // Se o nome de exibição for um IP e o endereço host não for um IP, houve inversão
        return IsStrictIpAddress(nameTrimmed) && !IsStrictIpAddress(hostTrimmed);
    }
}
