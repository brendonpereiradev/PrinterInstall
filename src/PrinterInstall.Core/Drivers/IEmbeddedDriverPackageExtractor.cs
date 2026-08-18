namespace PrinterInstall.Core.Drivers;

/// <summary>
/// Interface para extração e gerenciamento do cache de drivers embutidos no binário.
/// </summary>
public interface IEmbeddedDriverPackageExtractor
{
    /// <summary>
    /// Indica se os drivers embutidos já foram extraídos e validados no diretório de cache.
    /// </summary>
    bool IsExtracted { get; }

    /// <summary>
    /// Retorna o caminho do diretório de cache dos drivers extraídos, extraindo-os sincronamente se necessário.
    /// Retorna null se o recurso de drivers embutidos não existir ou falhar ao extrair.
    /// </summary>
    string? GetExtractedDriversPath();

    /// <summary>
    /// Garante de forma assíncrona que os drivers embutidos sejam descompactados no diretório de cache.
    /// </summary>
    Task<string?> EnsureExtractedAsync(CancellationToken cancellationToken = default);
}
