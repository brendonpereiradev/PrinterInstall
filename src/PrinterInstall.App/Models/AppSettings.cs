namespace PrinterInstall.App.Models;

/// <summary>
/// Representa as configurações de domínio e conectividade da aplicação.
/// </summary>
public sealed record AppSettings(
    string DomainName = "preventsenior.local",
    string? LdapHost = null);
