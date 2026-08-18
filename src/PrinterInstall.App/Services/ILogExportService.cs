namespace PrinterInstall.App.Services;

/// <summary>
/// Contrato para o serviço de exportação de log em arquivo.
/// </summary>
public interface ILogExportService
{
    /// <summary>
    /// Exibe caixa de diálogo para seleção de caminho e salva o conteúdo em arquivo de texto.
    /// </summary>
    LogExportResult ExportLog(string defaultFileName, string fileContent);
}
