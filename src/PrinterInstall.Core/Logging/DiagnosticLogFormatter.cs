using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace PrinterInstall.Core.Logging;

/// <summary>
/// Utilitário para formatação rica de exceções e diagnósticos de execução para logs e relatórios.
/// </summary>
public static class DiagnosticLogFormatter
{
    /// <summary>
    /// Formata uma exceção em uma descrição detalhada de uma linha, contendo códigos HResult, status WMI/Win32 e inner exceptions.
    /// </summary>
    public static string FormatException(Exception? ex)
    {
        if (ex is null)
            return string.Empty;

        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    parts.Add(FormatSingleException(inner));
                }
                break;
            }

            parts.Add(FormatSingleException(current));
        }

        return string.Join(" | ", parts.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// Formata detalhes técnicos completos de uma exceção, incluindo rastreamento de pilha (stack trace) quando disponível.
    /// </summary>
    public static string FormatExceptionDetails(Exception? ex)
    {
        if (ex is null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(FormatException(ex));

        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(ex.StackTrace.TrimEnd());
        }

        if (ex.InnerException is not null)
        {
            sb.AppendLine("Cadeia completa de exceções:");
            sb.AppendLine(ex.ToString());
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSingleException(Exception ex)
    {
        var typeName = ex.GetType().Name;
        var message = ex.Message?.Trim() ?? string.Empty;
        var codeDetails = new List<string>();

        if (ex is ManagementException mgmt)
        {
            codeDetails.Add($"WmiStatus: {mgmt.ErrorCode}");
        }

        if (ex is Win32Exception win32)
        {
            codeDetails.Add($"Win32Error: {win32.NativeErrorCode}");
        }

        if (ex is COMException com)
        {
            codeDetails.Add($"HResult: 0x{com.HResult:X8}");
        }
        else if (ex.HResult != 0 && ex.HResult != unchecked((int)0x80004005) && ex.HResult != unchecked((int)0x80131500))
        {
            codeDetails.Add($"HResult: 0x{ex.HResult:X8}");
        }

        var codeSuffix = codeDetails.Count > 0 ? $" [{string.Join(", ", codeDetails)}]" : string.Empty;

        if (string.IsNullOrWhiteSpace(message))
            return $"{typeName}{codeSuffix}";

        return $"{typeName}: {message}{codeSuffix}";
    }
}
