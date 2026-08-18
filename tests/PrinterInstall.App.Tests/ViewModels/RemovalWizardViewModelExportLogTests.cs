using System.Net;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using Xunit;

namespace PrinterInstall.App.Tests.ViewModels;

public class RemovalWizardViewModelExportLogTests
{
    private class FakeLogExportService : ILogExportService
    {
        public string? LastDefaultFileName { get; private set; }
        public string? LastContent { get; private set; }
        public LogExportResult ResultToReturn { get; set; } = LogExportResult.Succeeded(@"C:\Logs\remocao.txt");

        public LogExportResult ExportLog(string defaultFileName, string fileContent)
        {
            LastDefaultFileName = defaultFileName;
            LastContent = fileContent;
            return ResultToReturn;
        }
    }

    private static (RemovalWizardViewModel Sut, FakeLogExportService FakeExporter) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("operador", "senha", "corp"),
            DomainName = "corp"
        };
        var fakeExporter = new FakeLogExportService();

        var vm = new RemovalWizardViewModel(
            session,
            null!,
            null!,
            fakeExporter);

        return (vm, fakeExporter);
    }

    [Fact]
    public void CanExportLog_InitiallyFalseOnStepZero()
    {
        var (sut, _) = CreateSut();
        Assert.Equal(0, sut.CurrentStepIndex);
        Assert.False(sut.CanExportLog);
        Assert.False(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportLog_FalseOnStepThreeWhenLogIsEmpty()
    {
        var (sut, _) = CreateSut();
        sut.CurrentStepIndex = 3;
        sut.LogText = "";

        Assert.False(sut.CanExportLog);
        Assert.False(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportLog_FalseOnStepThreeWhenExecuting()
    {
        var (sut, _) = CreateSut();
        sut.CurrentStepIndex = 3;
        sut.LogText = "[10:00:00] Removendo...";
        sut.IsExecuting = true;

        Assert.False(sut.CanExportLog);
        Assert.False(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportLog_TrueOnStepThreeWhenNotExecutingAndHasLog()
    {
        var (sut, _) = CreateSut();
        sut.CurrentStepIndex = 3;
        sut.LogText = "[10:00:00] Removendo...";
        sut.IsExecuting = false;

        Assert.True(sut.CanExportLog);
        Assert.True(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void ExportLogCommand_ExecutesAndAppendsSuccessToLog()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Succeeded(@"C:\Users\Admin\Desktop\PrinterInstall_Controle.txt");
        sut.CurrentStepIndex = 3;
        sut.ReviewSummary = "PC-01: remover 'OldPrinter' (porta '10.0.0.1')";
        sut.LogText = "[10:00:00] Fila removida.\r\n";

        sut.ExportLogCommand.Execute(null);

        Assert.NotNull(fakeExporter.LastDefaultFileName);
        Assert.StartsWith("PrinterInstall_Controle_", fakeExporter.LastDefaultFileName);
        Assert.Contains("Controle e Remoção de Impressoras", fakeExporter.LastContent);
        Assert.Contains("PC-01: remover 'OldPrinter'", fakeExporter.LastContent);
        Assert.Contains("Fila removida.", fakeExporter.LastContent);
        Assert.Contains(@"Log exportado com sucesso para: C:\Users\Admin\Desktop\PrinterInstall_Controle.txt", sut.LogText);
    }

    [Fact]
    public void ExportLogCommand_UserCancelled_DoesNotAppendErrorMessage()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Cancelled();
        sut.CurrentStepIndex = 3;
        var initialLog = "[10:00:00] Log inicial.\r\n";
        sut.LogText = initialLog;

        sut.ExportLogCommand.Execute(null);

        Assert.Equal(initialLog, sut.LogText);
    }

    [Fact]
    public void ExportLogCommand_Failure_AppendsErrorToLog()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Failed("Caminho inválido");
        sut.CurrentStepIndex = 3;
        sut.LogText = "[10:00:00] Log inicial.\r\n";

        sut.ExportLogCommand.Execute(null);

        Assert.Contains("Falha ao exportar log: Caminho inválido", sut.LogText);
    }
}
