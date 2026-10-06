namespace PrinterInstall.Core.Logging;

/// <summary>
/// Contrato para geração e persistência de logs detalhados em disco para suporte e depuração.
/// </summary>
public interface IDiagnosticFileLogger
{
    /// <summary>
    /// Caminho da pasta onde os arquivos de log são mantidos.
    /// </summary>
    string LogDirectory { get; }

    /// <summary>
    /// Caminho do arquivo de log da sessão/dia atual.
    /// </summary>
    string CurrentLogFilePath { get; }

    string SessionId => string.Empty;

    /// <summary>Conteúdo desta sessão, independente do arquivo diário e de outras execuções.</summary>
    string ReadSessionLog() => string.Empty;
    void RegisterSensitiveValue(string? value) { }

    /// <summary>
    /// Grava uma entrada de log formatada em disco.
    /// </summary>
    void Log(DiagnosticLogLevel level, string message, string? category = null, Exception? exception = null);

    /// <summary>
    /// Grava um registro informativo.
    /// </summary>
    void LogInfo(string message, string? category = null);

    /// <summary>
    /// Grava um registro de depuração.
    /// </summary>
    void LogDebug(string message, string? category = null);

    /// <summary>
    /// Grava um registro de aviso.
    /// </summary>
    void LogWarning(string message, string? category = null, Exception? exception = null);

    /// <summary>
    /// Grava um registro de erro.
    /// </summary>
    void LogError(string message, string? category = null, Exception? exception = null);

    /// <summary>
    /// Remove arquivos de log antigos que excedam o período de retenção em dias.
    /// </summary>
    void CleanOldLogs(int retentionDays = 14);
}
