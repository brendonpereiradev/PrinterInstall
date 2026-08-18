using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PrinterInstall.Core.Logging;

/// <summary>
/// Formata relatórios de diagnóstico e logs de execução para exportação em texto (.txt).
/// </summary>
public static class LogReportFormatter
{
    private const string SeparatorMajor = "================================================================================";
    private const string SeparatorMinor = "--------------------------------------------------------------------------------";

    /// <summary>
    /// Gera o relatório completo para a operação de Deploy de Impressoras.
    /// </summary>
    public static string FormatDeployReport(
        string? operatorIdentity,
        string? localMachineName,
        IEnumerable<(string ComputerName, string PrinterQueueName, string State, string? Message)>? targets,
        string? logText,
        DateTime? exportTime = null)
    {
        var timestamp = (exportTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();

        AppendHeader(sb, "Deploy de Impressoras", timestamp, operatorIdentity, localMachineName);

        sb.AppendLine(SeparatorMinor);
        sb.AppendLine("RESUMO DOS ALVOS");
        sb.AppendLine(SeparatorMinor);

        var targetList = targets?.ToList() ?? new List<(string, string, string, string?)>();
        if (targetList.Count == 0)
        {
            sb.AppendLine("(Nenhum alvo processado)");
        }
        else
        {
            foreach (var target in targetList)
            {
                var queueDisplay = string.IsNullOrWhiteSpace(target.PrinterQueueName) ? "-" : target.PrinterQueueName;
                var msgDisplay = string.IsNullOrWhiteSpace(target.Message) ? "" : $" — Detalhes: {target.Message}";
                sb.AppendLine($"• [{target.ComputerName} | {queueDisplay}] Estado: {target.State}{msgDisplay}");
            }
        }
        sb.AppendLine();

        AppendLogSection(sb, logText);
        AppendFooter(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Gera o relatório completo para a operação de Controle/Remoção de Impressoras.
    /// </summary>
    public static string FormatRemovalReport(
        string? operatorIdentity,
        string? localMachineName,
        string? reviewSummary,
        string? logText,
        DateTime? exportTime = null)
    {
        var timestamp = (exportTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();

        AppendHeader(sb, "Controle e Remoção de Impressoras", timestamp, operatorIdentity, localMachineName);

        sb.AppendLine(SeparatorMinor);
        sb.AppendLine("PLANO / RESUMO DE AÇÕES");
        sb.AppendLine(SeparatorMinor);

        if (string.IsNullOrWhiteSpace(reviewSummary))
        {
            sb.AppendLine("(Nenhum resumo de ações registrado)");
        }
        else
        {
            sb.AppendLine(reviewSummary.TrimEnd());
        }
        sb.AppendLine();

        AppendLogSection(sb, logText);
        AppendFooter(sb);

        return sb.ToString();
    }

    private static void AppendHeader(
        StringBuilder sb,
        string operationTitle,
        string timestamp,
        string? operatorIdentity,
        string? localMachineName)
    {
        sb.AppendLine(SeparatorMajor);
        sb.AppendLine("PrinterInstall — Relatório de Diagnóstico e Log de Execução");
        sb.AppendLine(SeparatorMajor);
        sb.AppendLine($"Operação: {operationTitle}");
        sb.AppendLine($"Data/Hora da Exportação: {timestamp}");
        sb.AppendLine($"Operador: {(string.IsNullOrWhiteSpace(operatorIdentity) ? "Não informado" : operatorIdentity)}");
        sb.AppendLine($"Computador Local: {(string.IsNullOrWhiteSpace(localMachineName) ? Environment.MachineName : localMachineName)}");
        sb.AppendLine($"Ambiente do Sistema: {Environment.OSVersion} (.NET {Environment.Version})");
        sb.AppendLine();
    }

    private static void AppendLogSection(StringBuilder sb, string? logText)
    {
        sb.AppendLine(SeparatorMinor);
        sb.AppendLine("LOG DE EXECUÇÃO DETALHADO");
        sb.AppendLine(SeparatorMinor);

        if (string.IsNullOrWhiteSpace(logText))
        {
            sb.AppendLine("(Nenhum registro de log gerado)");
        }
        else
        {
            sb.AppendLine(logText.TrimEnd());
        }
        sb.AppendLine();
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine(SeparatorMajor);
        sb.AppendLine("Fim do Relatório");
        sb.AppendLine(SeparatorMajor);
    }
}
