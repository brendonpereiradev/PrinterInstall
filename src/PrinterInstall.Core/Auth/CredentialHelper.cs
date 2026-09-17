using System;
using System.Net;
using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Auth;

/// <summary>
/// Utilitário centralizado para sanitização, normalização e formatação de credenciais de domínio e CPF.
/// </summary>
public static class CredentialHelper
{
    /// <summary>
    /// Remove pontuações e formatações de CPF, mantendo apenas os dígitos.
    /// </summary>
    public static string SanitizeCpf(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        string trimmed = input.Trim();

        if (Regex.IsMatch(trimmed, @"^\d{3}\.\d{3}\.\d{3}[-\/]\d{2}$"))
        {
            return Regex.Replace(trimmed, @"[^\d]", string.Empty);
        }

        return trimmed;
    }

    /// <summary>
    /// Formata o usuário para autenticação de domínio (DOMINIO\usuario ou UPN), evitando duplicações como DOMINIO\DOMINIO\usuario.
    /// </summary>
    public static string FormatDomainUser(string? domain, string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return string.Empty;

        var cleanUser = userName.Trim();

        if (cleanUser.Contains('\\', StringComparison.Ordinal))
        {
            var parts = cleanUser.Split('\\', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return $@"{parts[0]}\{parts[1]}";
            }
            return cleanUser;
        }

        if (cleanUser.Contains('@', StringComparison.Ordinal))
        {
            return cleanUser;
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var cleanDomain = domain.Trim().TrimEnd('\\');
            return $@"{cleanDomain}\{cleanUser}";
        }

        return cleanUser;
    }

    /// <summary>
    /// Extrai o nome de usuário puro e o domínio a partir de uma entrada que pode conter DOMINIO\user, user@dominio ou user.
    /// </summary>
    public static (string UserName, string Domain) SplitDomainAndUser(string? rawUser, string? defaultDomain = null)
    {
        if (string.IsNullOrWhiteSpace(rawUser))
            return (string.Empty, defaultDomain?.Trim() ?? string.Empty);

        var trimmed = rawUser.Trim();

        if (trimmed.Contains('\\', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('\\', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return (parts[1], parts[0]);
        }

        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('@', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return (parts[0], parts[1]);
        }

        return (trimmed, defaultDomain?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Formata uma credencial NetworkCredential para uso em chamadas WMI, SMB e processos remotos.
    /// </summary>
    public static string BuildCredentialUserName(NetworkCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return FormatDomainUser(credential.Domain, credential.UserName);
    }
}
