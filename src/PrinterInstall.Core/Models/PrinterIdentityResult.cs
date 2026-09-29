namespace PrinterInstall.Core.Models;

public sealed record PrinterIdentityResult(
    string Host,
    PrinterBrand? Brand,
    string? Model,
    string Source,
    string? Detail = null,
    bool ConflictingEvidence = false)
{
    public bool IsIdentified => !ConflictingEvidence && Brand.HasValue && !string.IsNullOrWhiteSpace(Model);
}
