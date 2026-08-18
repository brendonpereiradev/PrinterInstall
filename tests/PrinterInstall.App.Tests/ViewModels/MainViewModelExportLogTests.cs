using System.Net;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;
using Xunit;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelExportLogTests
{
    private class FakeLogExportService : ILogExportService
    {
        public string? LastDefaultFileName { get; private set; }
        public string? LastContent { get; private set; }
        public LogExportResult ResultToReturn { get; set; } = LogExportResult.Succeeded(@"C:\Logs\test.txt");

        public LogExportResult ExportLog(string defaultFileName, string fileContent)
        {
            LastDefaultFileName = defaultFileName;
            LastContent = fileContent;
            return ResultToReturn;
        }
    }

    private static (MainViewModel Sut, FakeLogExportService FakeExporter) CreateSut()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("operador", "senha", "corp"),
            DomainName = "corp"
        };
        var identity = new LocalMachineIdentity();
        var fakeExporter = new FakeLogExportService();

        var vm = new MainViewModel(
            session,
            null!,
            null!,
            null!,
            identity,
            fakeExporter);

        return (vm, fakeExporter);
    }

    [Fact]
    public void CanExportLog_InitiallyFalseWhenLogTextIsEmpty()
    {
        var (sut, _) = CreateSut();
        Assert.False(sut.CanExportLog);
        Assert.False(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportLog_TrueWhenLogTextNotEmptyAndNotRunning()
    {
        var (sut, _) = CreateSut();
        sut.LogText = "[10:00:00] Evento de teste";

        Assert.True(sut.CanExportLog);
        Assert.True(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportLog_FalseWhenDeployIsRunningEvenIfLogHasContent()
    {
        var (sut, _) = CreateSut();
        sut.LogText = "[10:00:00] Evento";
        sut.IsDeployRunning = true;

        Assert.False(sut.CanExportLog);
        Assert.False(sut.ExportLogCommand.CanExecute(null));
    }

    [Fact]
    public void ExportLogCommand_ExecutesAndAppendsSuccessToLog()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Succeeded(@"C:\Users\Admin\Desktop\log.txt");
        sut.LogText = "[10:00:00] Instalação concluída com sucesso.";

        sut.ExportLogCommand.Execute(null);

        Assert.NotNull(fakeExporter.LastDefaultFileName);
        Assert.StartsWith("PrinterInstall_Deploy_", fakeExporter.LastDefaultFileName);
        Assert.Contains("Instalação concluída com sucesso.", fakeExporter.LastContent);
        Assert.Contains(@"Log exportado com sucesso para: C:\Users\Admin\Desktop\log.txt", sut.LogText);
    }

    [Fact]
    public void ExportLogCommand_UserCancelled_DoesNotAppendErrorMessage()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Cancelled();
        var initialLog = "[10:00:00] Log inicial.\r\n";
        sut.LogText = initialLog;

        sut.ExportLogCommand.Execute(null);

        Assert.Equal(initialLog, sut.LogText);
    }

    [Fact]
    public void ExportLogCommand_Failure_AppendsErrorToLog()
    {
        var (sut, fakeExporter) = CreateSut();
        fakeExporter.ResultToReturn = LogExportResult.Failed("Disco cheio ou sem permissão");
        sut.LogText = "[10:00:00] Log inicial.";

        sut.ExportLogCommand.Execute(null);

        Assert.Contains("Falha ao exportar log: Disco cheio ou sem permissão", sut.LogText);
    }
}
