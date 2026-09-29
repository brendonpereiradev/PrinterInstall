using System.IO;
using System.Security.Principal;
using System.Text;

namespace PrinterInstall.Core.Logging;

/// <summary>
/// Implementação de logger de diagnóstico contínuo em disco em %LOCALAPPDATA%\PrinterInstall\Logs.
/// </summary>
public sealed class DiagnosticFileLogger : IDiagnosticFileLogger
{
    private readonly object _syncRoot = new();
    private readonly string _logDirectory;
    private readonly Func<DateTime> _timeProvider;
    private bool _headerWritten;

    public string LogDirectory => _logDirectory;

    public string CurrentLogFilePath
    {
        get
        {
            var dateStr = _timeProvider().ToString("yyyy-MM-dd");
            return Path.Combine(_logDirectory, $"PrinterInstall_{dateStr}.log");
        }
    }

    public DiagnosticFileLogger(string? customLogDirectory = null, Func<DateTime>? timeProvider = null)
    {
        _timeProvider = timeProvider ?? (() => DateTime.Now);
        _logDirectory = !string.IsNullOrWhiteSpace(customLogDirectory)
            ? customLogDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrinterInstall",
                "Logs");

        EnsureDirectoryExists();
    }

    public void Log(DiagnosticLogLevel level, string message, string? category = null, Exception? exception = null)
    {
        lock (_syncRoot)
        {
            try
            {
                EnsureDirectoryExists();
                EnsureHeaderWritten();

                var filePath = CurrentLogFilePath;
                var now = _timeProvider();
                var catPrefix = !string.IsNullOrWhiteSpace(category) ? $"[{category}] " : string.Empty;
                var levelStr = level.ToString().ToUpperInvariant().PadRight(5);

                var sb = new StringBuilder();
                sb.Append($"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] {catPrefix}{message}");

                if (exception != null)
                {
                    sb.AppendLine();
                    sb.Append("  => DETALHES TÉCNICOS: ");
                    sb.Append(DiagnosticLogFormatter.FormatExceptionDetails(exception));
                }

                sb.AppendLine();

                File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Falhas na escrita de log não devem interromper o fluxo da aplicação.
            }
        }
    }

    public void LogInfo(string message, string? category = null) =>
        Log(DiagnosticLogLevel.Info, message, category);

    public void LogDebug(string message, string? category = null) =>
        Log(DiagnosticLogLevel.Debug, message, category);

    public void LogWarning(string message, string? category = null, Exception? exception = null) =>
        Log(DiagnosticLogLevel.Warning, message, category, exception);

    public void LogError(string message, string? category = null, Exception? exception = null) =>
        Log(DiagnosticLogLevel.Error, message, category, exception);

    public void CleanOldLogs(int retentionDays = 14)
    {
        if (retentionDays <= 0)
            return;

        lock (_syncRoot)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return;

                var cutoff = _timeProvider().AddDays(-retentionDays);
                var dir = new DirectoryInfo(_logDirectory);

                foreach (var file in dir.GetFiles("PrinterInstall_*.log"))
                {
                    if (file.LastWriteTime < cutoff)
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch
            {
                // Silencioso em caso de arquivo travado
            }
        }
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    private void EnsureHeaderWritten()
    {
        if (_headerWritten)
            return;

        var filePath = CurrentLogFilePath;
        if (!File.Exists(filePath))
        {
            var isElevated = CheckIsElevated();
            var header = new StringBuilder();
            header.AppendLine("================================================================================");
            header.AppendLine($"PrinterInstall — Sessão de Diagnóstico de Depuração");
            header.AppendLine($"Iniciado em: {_timeProvider():yyyy-MM-dd HH:mm:ss}");
            header.AppendLine($"Sistema Operacional: {Environment.OSVersion} (.NET {Environment.Version})");
            header.AppendLine($"Máquina Local: {Environment.MachineName}");
            header.AppendLine($"Usuário do Processo: {Environment.UserDomainName}\\{Environment.UserName}");
            header.AppendLine($"Processo Elevado (Admin): {(isElevated ? "SIM" : "NÃO")}");
            header.AppendLine("================================================================================");
            header.AppendLine();
            File.AppendAllText(filePath, header.ToString(), Encoding.UTF8);
        }

        _headerWritten = true;
    }

    private static bool CheckIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
