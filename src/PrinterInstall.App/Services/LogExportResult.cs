namespace PrinterInstall.App.Services;

/// <summary>
/// Representa o resultado de uma tentativa de exportação de log para arquivo.
/// </summary>
public sealed record LogExportResult
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public string? FilePath { get; init; }
    public string? ErrorMessage { get; init; }

    public static LogExportResult Succeeded(string filePath) =>
        new() { IsSuccess = true, IsCancelled = false, FilePath = filePath };

    public static LogExportResult Cancelled() =>
        new() { IsSuccess = false, IsCancelled = true };

    public static LogExportResult Failed(string errorMessage) =>
        new() { IsSuccess = false, IsCancelled = false, ErrorMessage = errorMessage };
}
