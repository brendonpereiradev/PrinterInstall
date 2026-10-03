using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.Core.Logging;

namespace PrinterInstall.App.ViewModels;

/// <summary>
/// Log de uma operação (implantação, controle de impressoras): acumula linhas com horário,
/// espelha no log de diagnóstico, exporta o relatório e abre a pasta de logs.
/// </summary>
public sealed partial class OperationLog : ObservableObject
{
    private readonly ILogExportService _exportService;
    private readonly IDiagnosticFileLogger? _diagnosticLogger;
    private readonly string _category;

    public OperationLog(ILogExportService exportService, IDiagnosticFileLogger? diagnosticLogger, string category)
    {
        _exportService = exportService;
        _diagnosticLogger = diagnosticLogger;
        _category = category;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyPropertyChangedFor(nameof(LastLine))]
    private string _text = "";

    public bool CanExport => !string.IsNullOrWhiteSpace(Text);

    /// <summary>Última linha registrada, usada como prévia quando o log está recolhido.</summary>
    public string LastLine
    {
        get
        {
            var trimmed = Text.TrimEnd();
            return trimmed[(trimmed.LastIndexOf('\n') + 1)..];
        }
    }

    public void Append(string line)
    {
        _diagnosticLogger?.LogInfo(line, _category);
        RunOnUi(() => Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\r\n");
    }

    /// <summary>Exporta o relatório montado por <paramref name="buildReport"/> e registra o resultado no próprio log.</summary>
    public void Export(string defaultFileName, Func<string, string> buildReport)
    {
        if (!CanExport)
            return;

        var result = _exportService.ExportLog(defaultFileName, buildReport(Text));

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.FilePath))
        {
            Append(string.Format(UiStrings.Log_ExportSuccessFormat, result.FilePath));
        }
        else if (!result.IsCancelled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            Append(string.Format(UiStrings.Log_ExportErrorFormat, result.ErrorMessage));
        }
    }

    public void OpenFolder()
    {
        try
        {
            var dir = _diagnosticLogger?.LogDirectory ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrinterInstall",
                "Logs");

            System.IO.Directory.CreateDirectory(dir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Append(string.Format(UiStrings.Log_OpenFolderErrorFormat, ex.Message));
        }
    }

    public static void RunOnUi(Action action)
    {
        if (Application.Current?.Dispatcher is not null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
