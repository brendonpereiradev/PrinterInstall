using PrinterInstall.Core.Remote;
using Xunit;

namespace PrinterInstall.Core.Tests.Remote;

public class DriverStagingScriptTests
{
    [Fact]
    public void BuildInstallerScript_GeneratesResilientZipExtractionWithOverwrite()
    {
        var infPath = @"C:\Windows\Temp\PrinterInstall\guid\driver.inf";
        var driverName = "EPSON Test Driver";
        var logPath = @"C:\Windows\Temp\PrinterInstall\guid\install.log";

        var script = WmiPrinterOperationsCore.BuildInstallerScript(infPath, driverName, logPath, skipRunAsBlock: true);

        Assert.Contains("ZipFile]::OpenRead($zipFile)", script);
        Assert.Contains("ZipFileExtensions]::ExtractToFile($entry, $targetPath, $true)", script);
        Assert.Contains("Remove-Item -LiteralPath $zipFile -Force", script);
    }
}
