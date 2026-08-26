using PrinterInstall.Core.Models;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class PrinterBrandHeuristicsValidatorTests
{
    [Theory]
    [InlineData("ETIQ_FARMACIA", PrinterBrand.Epson)]
    [InlineData("ETIQUETA_SALA1", PrinterBrand.Lexmark)]
    [InlineData("IMP_ETIQUETADORA", PrinterBrand.Brother)]
    [InlineData("TERMO_RECEPCAO", PrinterBrand.Epson)]
    [InlineData("TÉRMICA_TRIAGEM", PrinterBrand.Lexmark)]
    [InlineData("TERMICA_POSTO", PrinterBrand.Brother)]
    [InlineData("PULSEIRA_INTERNACAO", PrinterBrand.Epson)]
    [InlineData("GAINSCHA_LEITO", PrinterBrand.Lexmark)]
    [InlineData("ZEBRA_COLETA", PrinterBrand.Brother)]
    [InlineData("ARGOX_LAB", PrinterBrand.Epson)]
    [InlineData("etiq01", PrinterBrand.Epson)]
    [InlineData("imp-label-01", PrinterBrand.Lexmark)]
    public void HasSuspiciousBrandMismatch_LabelNameWithStandardBrand_ReturnsTrue(string displayName, PrinterBrand brand)
    {
        // Act
        var result = PrinterBrandHeuristicsValidator.HasSuspiciousBrandMismatch(displayName, brand, out var warning);

        // Assert
        Assert.True(result);
        Assert.NotNull(warning);
        Assert.Contains(displayName.Trim(), warning);
        Assert.Contains("Gainscha", warning);
    }

    [Theory]
    [InlineData("LASER_RECEPCAO", PrinterBrand.Gainscha)]
    [InlineData("LASERJET_ADM", PrinterBrand.Gainscha)]
    [InlineData("FOLHA_FATURAMENTO", PrinterBrand.Gainscha)]
    [InlineData("A4_CONTABIL", PrinterBrand.Gainscha)]
    [InlineData("MULTIFUNCIONAL_RH", PrinterBrand.Gainscha)]
    [InlineData("COPIADORA_DIRETORIA", PrinterBrand.Gainscha)]
    [InlineData("ECOTANK_SALA2", PrinterBrand.Gainscha)]
    [InlineData("Impress", PrinterBrand.Gainscha)]
    [InlineData("IMP_RECEPCAO", PrinterBrand.Gainscha)]
    [InlineData("imp01", PrinterBrand.Gainscha)]
    [InlineData("PRINTER_01", PrinterBrand.Gainscha)]
    [InlineData("PRT_SALA02", PrinterBrand.Gainscha)]
    [InlineData("IMPRESSORA_POSTO", PrinterBrand.Gainscha)]
    public void HasSuspiciousBrandMismatch_StandardNameWithGainscha_ReturnsTrue(string displayName, PrinterBrand brand)
    {
        // Act
        var result = PrinterBrandHeuristicsValidator.HasSuspiciousBrandMismatch(displayName, brand, out var warning);

        // Assert
        Assert.True(result);
        Assert.NotNull(warning);
        Assert.Contains(displayName.Trim(), warning);
    }

    [Theory]
    [InlineData("ETIQ_FARMACIA", PrinterBrand.Gainscha)]
    [InlineData("ETIQUETA_SALA1", PrinterBrand.Gainscha)]
    [InlineData("TERMO_RECEPCAO", PrinterBrand.Gainscha)]
    [InlineData("PULSEIRA_INTERNACAO", PrinterBrand.Gainscha)]
    [InlineData("GAINSCHA_TRIAGEM", PrinterBrand.Gainscha)]
    [InlineData("IMP_ETIQ_01", PrinterBrand.Gainscha)]
    [InlineData("IMP_ETIQUETADORA", PrinterBrand.Gainscha)]
    [InlineData("PRINTER_PULSEIRA_01", PrinterBrand.Gainscha)]
    [InlineData("IMP_TERMO_SALA1", PrinterBrand.Gainscha)]
    [InlineData("PRT_GAINSCHA_LEITO", PrinterBrand.Gainscha)]
    [InlineData("LASER_RECEPCAO", PrinterBrand.Epson)]
    [InlineData("LASER_RECEPCAO", PrinterBrand.Lexmark)]
    [InlineData("LASER_RECEPCAO", PrinterBrand.Brother)]
    [InlineData("A4_ADM", PrinterBrand.Epson)]
    [InlineData("MULTIFUNCIONAL_RH", PrinterBrand.Lexmark)]
    [InlineData("PRINTER_01", PrinterBrand.Epson)]
    [InlineData("SALA_MEDICA_02", PrinterBrand.Brother)]
    [InlineData(null, PrinterBrand.Epson)]
    [InlineData("", PrinterBrand.Gainscha)]
    [InlineData("   ", PrinterBrand.Lexmark)]
    public void HasSuspiciousBrandMismatch_ConsistentOrNeutral_ReturnsFalse(string? displayName, PrinterBrand brand)
    {
        // Act
        var result = PrinterBrandHeuristicsValidator.HasSuspiciousBrandMismatch(displayName, brand, out var warning);

        // Assert
        Assert.False(result);
        Assert.Null(warning);
    }

    [Fact]
    public void Inspect_MultipleDefinitions_ReturnsWarningsOnlyForMismatches()
    {
        // Arrange
        var definitions = new List<PrinterQueueDefinition>
        {
            new()
            {
                Brand = PrinterBrand.Epson,
                DisplayName = "ETIQ_TRIAGEM",
                PrinterHostAddress = "10.1.1.10",
                PortNumber = 9100,
                Protocol = TcpPrinterProtocol.Raw
            },
            new()
            {
                Brand = PrinterBrand.Gainscha,
                DisplayName = "ETIQ_POSTO",
                PrinterHostAddress = "10.1.1.11",
                PortNumber = 9100,
                Protocol = TcpPrinterProtocol.Raw,
                GainschaLabelPreset = GainschaLabelPreset.Paciente
            },
            new()
            {
                Brand = PrinterBrand.Gainscha,
                DisplayName = "LASER_ADM",
                PrinterHostAddress = "10.1.1.12",
                PortNumber = 9100,
                Protocol = TcpPrinterProtocol.Raw,
                GainschaLabelPreset = GainschaLabelPreset.Paciente
            },
            new()
            {
                Brand = PrinterBrand.Lexmark,
                DisplayName = "PRT_CONSULTORIO_1",
                PrinterHostAddress = "10.1.1.13",
                PortNumber = 9100,
                Protocol = TcpPrinterProtocol.Raw
            }
        };

        // Act
        var warnings = PrinterBrandHeuristicsValidator.Inspect(definitions);

        // Assert
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("ETIQ_TRIAGEM"));
        Assert.Contains(warnings, w => w.Contains("LASER_ADM"));
    }

    [Fact]
    public void Inspect_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(PrinterBrandHeuristicsValidator.Inspect(null));
        Assert.Empty(PrinterBrandHeuristicsValidator.Inspect(new List<PrinterQueueDefinition>()));
    }
}
