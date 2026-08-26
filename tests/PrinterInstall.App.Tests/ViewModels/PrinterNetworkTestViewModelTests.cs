using Moq;
using PrinterInstall.App.Resources;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.App.Tests.ViewModels;

/// <summary>
/// Testes unitários para <see cref="PrinterNetworkTestViewModel"/>.
/// </summary>
public class PrinterNetworkTestViewModelTests
{
    private readonly Mock<IDirectRawPrinterTestService> _mockTestService = new();
    private readonly Mock<IConfirmationDialogService> _mockDialogService = new();

    public PrinterNetworkTestViewModelTests()
    {
        _mockDialogService
            .Setup(x => x.ConfirmNetworkTestAsync(It.IsAny<string>(), It.IsAny<PrinterBrand>(), It.IsAny<GainschaLabelPreset?>()))
            .ReturnsAsync(true);
    }

    private PrinterNetworkTestViewModel CreateSut() => new(_mockTestService.Object, _mockDialogService.Object);

    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        Assert.Equal(PrinterBrand.Epson, sut.SelectedBrand);
        Assert.Equal(string.Empty, sut.HostAddress);
        Assert.Equal(string.Empty, sut.StatusMessage);
        Assert.False(sut.IsRunning);
        Assert.Equal(GainschaLabelPreset.Paciente, sut.SelectedGainschaLabelPreset);
        Assert.False(sut.IsGainschaBrand);
        Assert.False(sut.CanRun);
        Assert.False(sut.RunTestCommand.CanExecute(null));
        Assert.NotEmpty(sut.BrandChoices);
        Assert.NotEmpty(sut.GainschaLabelPresetChoices);
    }

    [Fact]
    public void ChangingHostAddress_UpdatesCanRunAndCanExecute()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        sut.HostAddress = "192.168.1.50";

        // Assert
        Assert.True(sut.CanRun);
        Assert.True(sut.RunTestCommand.CanExecute(null));

        // Act - limpa o endereço
        sut.HostAddress = "   ";

        // Assert
        Assert.False(sut.CanRun);
        Assert.False(sut.RunTestCommand.CanExecute(null));
    }

    [Fact]
    public void ChangingBrandToGainscha_UpdatesIsGainschaBrand()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        sut.SelectedBrand = PrinterBrand.Gainscha;

        // Assert
        Assert.True(sut.IsGainschaBrand);

        // Act - volta para Epson
        sut.SelectedBrand = PrinterBrand.Epson;

        // Assert
        Assert.False(sut.IsGainschaBrand);
    }

    [Fact]
    public async Task RunTestAsync_WhenHostIsEmpty_SetsValidationMessageAndDoesNotCallService()
    {
        // Arrange
        var sut = CreateSut();
        sut.HostAddress = "";

        // Act
        await sut.RunTestCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(UiStrings.NetworkTest_Validation_HostRequired, sut.StatusMessage);
        _mockTestService.Verify(
            x => x.RunAsync(It.IsAny<string>(), It.IsAny<PrinterBrand>(), It.IsAny<GainschaLabelPreset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTestAsync_WhenEpson_CallsServiceWithNullGainschaPresetAndSetsSuccessStatus()
    {
        // Arrange
        var sut = CreateSut();
        sut.HostAddress = " 10.0.0.25 ";
        sut.SelectedBrand = PrinterBrand.Epson;

        var expectedResult = new DirectRawPrinterTestResult
        {
            Success = true,
            FailedPhase = DirectRawPrinterTestPhase.None,
            Message = "Página de teste enviada com sucesso para 10.0.0.25:9100 (Epson ESC/POS)."
        };

        _mockTestService
            .Setup(x => x.RunAsync("10.0.0.25", PrinterBrand.Epson, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        await sut.RunTestCommand.ExecuteAsync(null);

        // Assert
        Assert.False(sut.IsRunning);
        Assert.Equal(expectedResult.Message, sut.StatusMessage);
        _mockTestService.Verify(
            x => x.RunAsync("10.0.0.25", PrinterBrand.Epson, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTestAsync_WhenGainscha_CallsServiceWithSelectedPreset()
    {
        // Arrange
        var sut = CreateSut();
        sut.HostAddress = "10.0.0.30";
        sut.SelectedBrand = PrinterBrand.Gainscha;
        sut.SelectedGainschaLabelPreset = GainschaLabelPreset.Pulseira;

        var expectedResult = new DirectRawPrinterTestResult
        {
            Success = true,
            FailedPhase = DirectRawPrinterTestPhase.None,
            Message = "Página de teste Gainscha enviada."
        };

        _mockTestService
            .Setup(x => x.RunAsync("10.0.0.30", PrinterBrand.Gainscha, GainschaLabelPreset.Pulseira, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        await sut.RunTestCommand.ExecuteAsync(null);

        // Assert
        Assert.False(sut.IsRunning);
        Assert.Equal(expectedResult.Message, sut.StatusMessage);
        _mockTestService.Verify(
            x => x.RunAsync("10.0.0.30", PrinterBrand.Gainscha, GainschaLabelPreset.Pulseira, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTestAsync_WhenCancelled_SetsCancelledStatus()
    {
        // Arrange
        var sut = CreateSut();
        sut.HostAddress = "10.0.0.40";
        sut.SelectedBrand = PrinterBrand.Brother;

        _mockTestService
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<PrinterBrand>(), It.IsAny<GainschaLabelPreset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        await sut.RunTestCommand.ExecuteAsync(null);

        // Assert
        Assert.False(sut.IsRunning);
        Assert.Equal(UiStrings.NetworkTest_Cancelled, sut.StatusMessage);
    }

    [Fact]
    public async Task CancelTestCommand_CancelsRunningOperation()
    {
        // Arrange
        var sut = CreateSut();
        var tcs = new TaskCompletionSource<DirectRawPrinterTestResult>();

        _mockTestService
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<PrinterBrand>(), It.IsAny<GainschaLabelPreset?>(), It.IsAny<CancellationToken>()))
            .Returns<string, PrinterBrand, GainschaLabelPreset?, CancellationToken>((h, b, p, ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        sut.HostAddress = "10.0.0.50";

        // Act - Inicia teste assincronamente e cancela em seguida
        var task = sut.RunTestCommand.ExecuteAsync(null);
        Assert.True(sut.IsRunning);

        sut.CancelTestCommand.Execute(null);

        // Aguarda finalização
        await task;

        // Assert
        Assert.False(sut.IsRunning);
        Assert.Equal(UiStrings.NetworkTest_Cancelled, sut.StatusMessage);
    }

    [Fact]
    public async Task RunTestAsync_WhenUserRejectsConfirmation_SetsCancelledStatusAndDoesNotRunTest()
    {
        // Arrange
        _mockDialogService
            .Setup(x => x.ConfirmNetworkTestAsync("10.0.0.99", PrinterBrand.Epson, null))
            .ReturnsAsync(false);

        var sut = CreateSut();
        sut.HostAddress = "10.0.0.99";
        sut.SelectedBrand = PrinterBrand.Epson;

        // Act
        await sut.RunTestCommand.ExecuteAsync(null);

        // Assert
        Assert.False(sut.IsRunning);
        Assert.Equal(UiStrings.NetworkTest_Cancelled, sut.StatusMessage);
        _mockTestService.Verify(
            x => x.RunAsync(It.IsAny<string>(), It.IsAny<PrinterBrand>(), It.IsAny<GainschaLabelPreset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GainschaLabelPresetChoices_ContainsMatrixAndLote()
    {
        // Arrange & Act
        var sut = CreateSut();
        var choices = sut.GainschaLabelPresetChoices.ToList();

        // Assert
        Assert.Contains(GainschaLabelPreset.Paciente, choices);
        Assert.Contains(GainschaLabelPreset.Matrix, choices);
        Assert.Contains(GainschaLabelPreset.Lote, choices);
        Assert.Contains(GainschaLabelPreset.Pulseira, choices);
    }
}
