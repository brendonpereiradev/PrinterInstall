using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PrinterInstall.App.Services;

/// <summary>
/// Implementação do serviço de exportação de log usando SaveFileDialog nativo do Windows.
/// </summary>
public class LogExportService : ILogExportService
{
    public LogExportResult ExportLog(string defaultFileName, string fileContent)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Exportar Log de Execução",
                Filter = "Arquivos de Texto (*.txt)|*.txt|Todos os Arquivos (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = defaultFileName
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return LogExportResult.Cancelled();
            }

            File.WriteAllText(dialog.FileName, fileContent, Encoding.UTF8);
            return LogExportResult.Succeeded(dialog.FileName);
        }
        catch (Exception ex)
        {
            return LogExportResult.Failed(ex.Message);
        }
    }
}
