using PrinterInstall.App.Services;
using PrinterInstall.Core.Models;
using Xunit;

namespace PrinterInstall.App.Tests.Services;

public class ConfirmationDialogServiceTests
{
    [Fact]
    public void Implements_IConfirmationDialogService()
    {
        var sut = new ConfirmationDialogService();
        Assert.IsAssignableFrom<IConfirmationDialogService>(sut);
    }

    [Fact]
    public async Task ConfirmDeployWarningAsync_WhenWarningsEmpty_ReturnsTrueImmediately()
    {
        var sut = new ConfirmationDialogService();
        var result = await sut.ConfirmDeployWarningAsync(Array.Empty<string>());
        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmDeployWarningAsync_WhenAppCurrentNull_ReturnsTrueGracefully()
    {
        var sut = new ConfirmationDialogService();
        var result = await sut.ConfirmDeployWarningAsync(new[] { "Possível divergência detectada" });
        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmNetworkTestAsync_WhenAppCurrentNull_ReturnsTrueGracefully()
    {
        var sut = new ConfirmationDialogService();
        var result = await sut.ConfirmNetworkTestAsync("10.0.0.50", PrinterBrand.Epson, null);
        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmNetworkTestAsync_GainschaWithPreset_WhenAppCurrentNull_ReturnsTrueGracefully()
    {
        var sut = new ConfirmationDialogService();
        var result = await sut.ConfirmNetworkTestAsync("10.0.0.50", PrinterBrand.Gainscha, GainschaLabelPreset.Paciente);
        Assert.True(result);
    }
}
