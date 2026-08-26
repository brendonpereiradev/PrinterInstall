using System.IO.Compression;
using System.Reflection;
using Moq;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Drivers;

public class EmbeddedDriverPackageExtractorTests : IDisposable
{
    private readonly string _tempDirectory;

    public EmbeddedDriverPackageExtractorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignora falhas de limpeza em arquivos temporários de teste
        }
    }

    [Fact]
    public async Task EnsureExtractedAsync_WhenResourceNotFound_ReturnsNull()
    {
        // Assembly do xUnit não contém o recurso de drivers
        var sut = new EmbeddedDriverPackageExtractor(_tempDirectory, typeof(FactAttribute).Assembly);

        var result = await sut.EnsureExtractedAsync();

        Assert.Null(result);
        Assert.False(sut.IsExtracted);
    }

    [Fact]
    public void Catalog_WhenLocalFolderMissing_UsesExtractedDriversFromExtractor()
    {
        var extractedRoot = Path.Combine(_tempDirectory, "ExtractedDrivers");
        var brotherFolder = Path.Combine(extractedRoot, "Brother");
        Directory.CreateDirectory(brotherFolder);
        File.WriteAllText(Path.Combine(brotherFolder, "BROHL20A.INF"), "");

        var mockExtractor = new Mock<IEmbeddedDriverPackageExtractor>();
        mockExtractor.Setup(x => x.GetExtractedDriversPath()).Returns(extractedRoot);

        var emptyBaseDir = Path.Combine(_tempDirectory, "EmptyBase");
        Directory.CreateDirectory(emptyBaseDir);

        var catalog = new LocalDriverPackageCatalog(emptyBaseDir, mockExtractor.Object);
        var pkg = catalog.TryGet(PrinterBrand.Brother);

        Assert.NotNull(pkg);
        Assert.Equal(PrinterBrand.Brother, pkg!.Brand);
        Assert.Equal("BROHL20A.INF", pkg.InfFileName);
        Assert.Equal(brotherFolder, pkg.RootFolder);
    }

    [Fact]
    public void Catalog_WhenExternalFolderExists_PrioritizesExternalOverEmbedded()
    {
        // 1. Cria pasta externa ao lado do baseDir
        var baseDir = Path.Combine(_tempDirectory, "AppBase");
        var externalBrother = Path.Combine(baseDir, "Drivers", "Brother");
        Directory.CreateDirectory(externalBrother);
        File.WriteAllText(Path.Combine(externalBrother, "EXTERNAL.INF"), "");

        // 2. Cria pasta do extrator embutido
        var extractedRoot = Path.Combine(_tempDirectory, "ExtractedDrivers");
        var embeddedBrother = Path.Combine(extractedRoot, "Brother");
        Directory.CreateDirectory(embeddedBrother);
        File.WriteAllText(Path.Combine(embeddedBrother, "EMBEDDED.INF"), "");

        var mockExtractor = new Mock<IEmbeddedDriverPackageExtractor>();
        mockExtractor.Setup(x => x.GetExtractedDriversPath()).Returns(extractedRoot);

        var catalog = new LocalDriverPackageCatalog(baseDir, mockExtractor.Object);
        var pkg = catalog.TryGet(PrinterBrand.Brother);

        Assert.NotNull(pkg);
        Assert.Equal("EXTERNAL.INF", pkg!.InfFileName);
        Assert.Equal(externalBrother, pkg.RootFolder);
    }

    [Fact]
    public void IsExtracted_WhenMarkerFileDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var sut = new EmbeddedDriverPackageExtractor(_tempDirectory, typeof(FactAttribute).Assembly);

        // Act & Assert
        Assert.False(sut.IsExtracted);
    }

    [Fact]
    public void IsExtracted_WhenMarkerFileExistsAndResourceStreamNull_ReturnsTrue()
    {
        // Arrange
        var sut = new EmbeddedDriverPackageExtractor(_tempDirectory, typeof(FactAttribute).Assembly);
        var markerPath = Path.Combine(_tempDirectory, ".extracted");
        File.WriteAllText(markerPath, "12345");

        // Act & Assert
        Assert.True(sut.IsExtracted);
    }

    [Fact]
    public void GetExtractedDriversPath_WhenMarkerExists_ReturnsTargetDirectory()
    {
        // Arrange
        var sut = new EmbeddedDriverPackageExtractor(_tempDirectory, typeof(FactAttribute).Assembly);
        var markerPath = Path.Combine(_tempDirectory, ".extracted");
        File.WriteAllText(markerPath, "12345");

        // Act
        var result = sut.GetExtractedDriversPath();

        // Assert
        Assert.Equal(_tempDirectory, result);
    }

    [Fact]
    public void GetExtractedDriversPath_WhenNoMarkerAndNoResource_ReturnsNull()
    {
        // Arrange
        var sut = new EmbeddedDriverPackageExtractor(_tempDirectory, typeof(FactAttribute).Assembly);

        // Act
        var result = sut.GetExtractedDriversPath();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DefaultConstructor_CreatesInstanceWithoutThrowing()
    {
        // Arrange & Act
        var sut = new EmbeddedDriverPackageExtractor();

        // Assert
        Assert.NotNull(sut);
    }
}
