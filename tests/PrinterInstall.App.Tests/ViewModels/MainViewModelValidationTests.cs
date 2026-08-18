using System.Net;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelValidationTests
{
    private static MainViewModel CreateSut(SessionContext session)
    {
        var identity = new LocalMachineIdentity();

        return new MainViewModel(
            session,
            null!,
            null!,
            null!,
            identity);
    }

    [Fact]
    public async Task DeployAsync_InvertedDisplayNameAndHost_BlocksAndLogsInversionWarning()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var sut = CreateSut(session);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "10.1.152.218";
        sut.PrinterRows[0].PrinterHostAddress = "Multifuncional";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Contains("Inversão detectada", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Targets);
    }

    [Fact]
    public async Task DeployAsync_InvalidHostAddressWithSpacesAndAccents_BlocksAndLogsInvalidHost()
    {
        var session = new SessionContext
        {
            Credential = new NetworkCredential("admin", "pass", "corp"),
            DomainName = "corp"
        };

        var sut = CreateSut(session);

        sut.ComputersText = "target-pc";
        sut.PrinterRows[0].Brand = PrinterBrand.Epson;
        sut.PrinterRows[0].DisplayName = "Multifuncional";
        sut.PrinterRows[0].PrinterHostAddress = "Consultório 6";

        await sut.DeployCommand.ExecuteAsync(null);

        Assert.Contains("inválido", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Targets);
    }
}
