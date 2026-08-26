using PrinterInstall.App.Services;

namespace PrinterInstall.App.Tests.Services;

/// <summary>
/// Testes unitários para <see cref="LogExportResult"/> e <see cref="LogExportService"/>.
/// </summary>
public class LogExportServiceTests
{
    [Fact]
    public void LogExportResult_Succeeded_SetsExpectedProperties()
    {
        // Arrange & Act
        var result = LogExportResult.Succeeded(@"C:\Logs\deploy_2026.txt");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Equal(@"C:\Logs\deploy_2026.txt", result.FilePath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void LogExportResult_Cancelled_SetsExpectedProperties()
    {
        // Arrange & Act
        var result = LogExportResult.Cancelled();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsCancelled);
        Assert.Null(result.FilePath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void LogExportResult_Failed_SetsExpectedProperties()
    {
        // Arrange & Act
        var result = LogExportResult.Failed("Caminho inválido ou sem permissão de escrita");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Null(result.FilePath);
        Assert.Equal("Caminho inválido ou sem permissão de escrita", result.ErrorMessage);
    }

    [Fact]
    public void LogExportService_ImplementsILogExportService()
    {
        // Arrange & Act
        var sut = new LogExportService();

        // Assert
        Assert.IsAssignableFrom<ILogExportService>(sut);
    }
}
