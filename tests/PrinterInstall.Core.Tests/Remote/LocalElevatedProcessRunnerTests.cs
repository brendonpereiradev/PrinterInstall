using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

/// <summary>
/// Testes unitários para <see cref="LocalElevatedProcessRunner"/>.
/// </summary>
public class LocalElevatedProcessRunnerTests
{
    private readonly LocalElevatedProcessRunner _sut = new();

    [Fact]
    public async Task RunScriptAsync_WhenStagingIsNull_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.RunScriptAsync(null!, "Write-Output 'test'", TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task RunScriptAsync_WhenScriptWritesOkResult_CompletesAndCleansUpStaging()
    {
        // Arrange
        var staging = LocalElevatedStagingPaths.Create();
        var stagingRoot = staging.Root;
        Assert.True(Directory.Exists(stagingRoot));

        // Script Powershell que escreve RESULT>> OK na saída capturada pelo transcript
        var scriptContent = "Write-Output 'RESULT>> OK'";

        // Act
        await _sut.RunScriptAsync(staging, scriptContent, TimeSpan.FromSeconds(15), CancellationToken.None);

        // Assert - O diretório staging deve ter sido limpo no bloco finally
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Fact]
    public async Task RunScriptAsync_WhenScriptWritesFailResult_ThrowsInvalidOperationException()
    {
        // Arrange
        var staging = LocalElevatedStagingPaths.Create();
        var stagingRoot = staging.Root;

        var scriptContent = "Write-Output 'RESULT>> FAIL erro simulado no teste'";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RunScriptAsync(staging, scriptContent, TimeSpan.FromSeconds(15), CancellationToken.None));

        Assert.Contains("erro simulado no teste", ex.Message);
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Fact]
    public async Task RunScriptAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var staging = LocalElevatedStagingPaths.Create();
        var stagingRoot = staging.Root;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.RunScriptAsync(staging, "Write-Output 'test'", TimeSpan.FromSeconds(5), cts.Token));

        // Staging deve ser limpo no finally
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Fact]
    public void Constructor_InitializesCorrectly()
    {
        // Arrange & Act
        var runner = new LocalElevatedProcessRunner();

        // Assert
        Assert.NotNull(runner);
    }
}
