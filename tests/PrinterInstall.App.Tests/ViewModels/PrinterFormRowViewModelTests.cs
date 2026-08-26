using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.App.Tests.ViewModels;

/// <summary>
/// Testes unitários para <see cref="PrinterFormRowViewModel"/>.
/// </summary>
public class PrinterFormRowViewModelTests
{
    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        // Arrange & Act
        var sut = new PrinterFormRowViewModel();

        // Assert
        Assert.Equal(PrinterBrand.Epson, sut.Brand);
        Assert.Equal(string.Empty, sut.DisplayName);
        Assert.Equal(string.Empty, sut.PrinterHostAddress);
        Assert.Null(sut.GainschaLabelPreset);
        Assert.False(sut.IsGainschaBrand);
        Assert.NotEmpty(PrinterFormRowViewModel.BrandChoices);
        Assert.NotEmpty(PrinterFormRowViewModel.GainschaLabelPresetChoices);
    }

    [Fact]
    public void Setters_UpdatePropertiesCorrectly()
    {
        // Arrange
        var sut = new PrinterFormRowViewModel();

        // Act
        sut.DisplayName = "Recepção Central";
        sut.PrinterHostAddress = "10.1.152.50";

        // Assert
        Assert.Equal("Recepção Central", sut.DisplayName);
        Assert.Equal("10.1.152.50", sut.PrinterHostAddress);
    }

    [Fact]
    public void SettingBrandToGainscha_EnablesIsGainschaBrandAndRetainsAssignedPreset()
    {
        // Arrange
        var sut = new PrinterFormRowViewModel();

        // Act
        sut.Brand = PrinterBrand.Gainscha;
        sut.GainschaLabelPreset = GainschaLabelPreset.Pulseira;

        // Assert
        Assert.True(sut.IsGainschaBrand);
        Assert.Equal(GainschaLabelPreset.Pulseira, sut.GainschaLabelPreset);
    }

    [Fact]
    public void SwitchingBrandFromGainschaToOtherBrand_ResetsPresetToNull()
    {
        // Arrange
        var sut = new PrinterFormRowViewModel
        {
            Brand = PrinterBrand.Gainscha,
            GainschaLabelPreset = GainschaLabelPreset.Matrix
        };
        Assert.True(sut.IsGainschaBrand);
        Assert.NotNull(sut.GainschaLabelPreset);

        // Act - Muda de Gainscha para Lexmark
        sut.Brand = PrinterBrand.Lexmark;

        // Assert
        Assert.False(sut.IsGainschaBrand);
        Assert.Null(sut.GainschaLabelPreset);
    }

    [Fact]
    public void StaticChoices_ContainExpectedCatalogValues()
    {
        // Assert
        Assert.Equal(Enum.GetValues<PrinterBrand>(), PrinterFormRowViewModel.BrandChoices);
        Assert.Equal(GainschaLabelPresetCatalog.UiDisplayOrder, PrinterFormRowViewModel.GainschaLabelPresetChoices);
    }
}
