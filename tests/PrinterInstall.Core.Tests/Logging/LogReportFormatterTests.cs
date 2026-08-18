using System;
using System.Collections.Generic;
using PrinterInstall.Core.Logging;
using Xunit;

namespace PrinterInstall.Core.Tests.Logging;

public class LogReportFormatterTests
{
    [Fact]
    public void FormatDeployReport_IncludesAllSectionsAndTargetDetails()
    {
        // Arrange
        var operatorId = @"PREVENTSENIOR\operador.teste";
        var localMachine = "PC-TI-ADMIN";
        var targets = new List<(string ComputerName, string PrinterQueueName, string State, string? Message)>
        {
            ("PC-RH-01", "EPSON_RECEPCAO", "CompletedSuccess", "Fila criada com sucesso"),
            ("PC-RH-02", "LEXMARK_TRIAGEM", "Error", "Acesso negado (0x80070005)")
        };
        var logText = "[2026-08-18 10:00:00] Iniciando deploy...\r\n[2026-08-18 10:00:05] Concluído.";
        var exportTime = new DateTime(2026, 8, 18, 10, 0, 10);

        // Act
        var result = LogReportFormatter.FormatDeployReport(operatorId, localMachine, targets, logText, exportTime);

        // Assert
        Assert.Contains("Deploy de Impressoras", result);
        Assert.Contains("Data/Hora da Exportação: 2026-08-18 10:00:10", result);
        Assert.Contains(@"Operador: PREVENTSENIOR\operador.teste", result);
        Assert.Contains("Computador Local: PC-TI-ADMIN", result);
        Assert.Contains("RESUMO DOS ALVOS", result);
        Assert.Contains("• [PC-RH-01 | EPSON_RECEPCAO] Estado: CompletedSuccess — Detalhes: Fila criada com sucesso", result);
        Assert.Contains("• [PC-RH-02 | LEXMARK_TRIAGEM] Estado: Error — Detalhes: Acesso negado (0x80070005)", result);
        Assert.Contains("LOG DE EXECUÇÃO DETALHADO", result);
        Assert.Contains("[2026-08-18 10:00:00] Iniciando deploy...", result);
        Assert.Contains("Fim do Relatório", result);
    }

    [Fact]
    public void FormatDeployReport_HandlesNullOrEmptyFieldsGracefully()
    {
        // Act
        var result = LogReportFormatter.FormatDeployReport(null, null, null, null, new DateTime(2026, 1, 1, 12, 0, 0));

        // Assert
        Assert.Contains("Operador: Não informado", result);
        Assert.Contains("(Nenhum alvo processado)", result);
        Assert.Contains("(Nenhum registro de log gerado)", result);
    }

    [Fact]
    public void FormatRemovalReport_IncludesReviewSummaryAndLogText()
    {
        // Arrange
        var operatorId = @"PREVENTSENIOR\admin";
        var localMachine = "PC-LOCAL";
        var reviewSummary = "PC-01: remover 'OldPrinter' (porta '10.0.0.1')\r\nPC-02: renomear 'P1' → 'P2'";
        var logText = "[10:00:00] Removendo fila...\r\n[10:00:02] Operações concluídas.";
        var exportTime = new DateTime(2026, 8, 18, 11, 0, 0);

        // Act
        var result = LogReportFormatter.FormatRemovalReport(operatorId, localMachine, reviewSummary, logText, exportTime);

        // Assert
        Assert.Contains("Controle e Remoção de Impressoras", result);
        Assert.Contains("Data/Hora da Exportação: 2026-08-18 11:00:00", result);
        Assert.Contains(@"Operador: PREVENTSENIOR\admin", result);
        Assert.Contains("PLANO / RESUMO DE AÇÕES", result);
        Assert.Contains("PC-01: remover 'OldPrinter'", result);
        Assert.Contains("PC-02: renomear 'P1' → 'P2'", result);
        Assert.Contains("LOG DE EXECUÇÃO DETALHADO", result);
        Assert.Contains("[10:00:00] Removendo fila...", result);
        Assert.Contains("Fim do Relatório", result);
    }

    [Fact]
    public void FormatRemovalReport_HandlesNullSummaryGracefully()
    {
        // Act
        var result = LogReportFormatter.FormatRemovalReport(null, null, null, "log line", new DateTime(2026, 1, 1, 12, 0, 0));

        // Assert
        Assert.Contains("(Nenhum resumo de ações registrado)", result);
        Assert.Contains("log line", result);
    }
}
