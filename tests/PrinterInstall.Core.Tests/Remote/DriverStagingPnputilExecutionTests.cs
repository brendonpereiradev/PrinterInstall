using System.Diagnostics;
using System.Text;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class DriverStagingPnputilExecutionTests
{
    private const string ExistingPackageOutput = "Pacote de driver adicionado com Ûxito. (Jß existe no sistema)\nNome Publicado: oem206.inf\nO pacote de driver estß atualizado no dispositivo: SWD\\PRINTENUM\\test\nTotal de pacotes de driver: 1\nPacotes de driver adicionados: 0";

    [Theory]
    [InlineData(0, "Pacotes de driver adicionados: 0", true)]
    [InlineData(3010, "Pacotes de driver adicionados: 1", true)]
    [InlineData(0, "Treiberpaket erfolgreich hinzugefügt.", true)]
    [InlineData(5, "Falha ao adicionar pacote de driver: Acesso negado.\nPacotes de driver adicionados: 0", false)]
    [InlineData(2, "Arquivo ausente.\nPacotes de driver adicionados: 0", false)]
    [InlineData(259, ExistingPackageOutput, true)]
    [InlineData(259, "Pacotes de driver adicionados: 0", false)]
    [InlineData(259, "Falha ao adicionar pacote de driver: Acesso negado.\noem206.inf", false)]
    public async Task GeneratedPnputilHandling_PreservesFailureAndAllowsSuccessfulStaging(int code, string output, bool success)
    {
        var installer = WmiPrinterOperationsCore.BuildInstallerScript(@"C:\test\driver.inf", "Driver", @"C:\test\install.log", true);
        var start = installer.IndexOf("    $pnpOutput =", StringComparison.Ordinal);
        var end = installer.IndexOf("    function Test-DriverRegistered", start, StringComparison.Ordinal);
        // Executa apenas a interpretação do resultado, sem instalar drivers ou alterar o Windows.
        var block = installer[start..end].Replace("& pnputil.exe", "Invoke-FakePnputil", StringComparison.Ordinal);
        var script = "$ProgressPreference = 'SilentlyContinue'\n"
            + $"function Invoke-FakePnputil {{ $global:LASTEXITCODE = {code}; '{WmiPrinterOperationsCore.EscapePs(output)}' }}\n"
            + "function Stop-Transcript {}\n" + block + "Write-Output 'CONTINUE>> spooler'\nexit 0";
        var (exitCode, result) = await RunScriptAsync(script);
        Assert.Equal(success ? 0 : 1, exitCode);
        Assert.Equal(success, result.Contains("CONTINUE>> spooler", StringComparison.Ordinal));
        if (!success)
        {
            var detail = WmiPrinterOperationsCore.ExtractResultLine(result);
            Assert.Contains($"pnputil exit code {code}", detail);
            Assert.Contains(output.Split('\n')[0], detail);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingPackage259_OnlySucceedsAfterSpoolerConfirmsDriver(bool registered)
    {
        var installer = WmiPrinterOperationsCore.BuildInstallerScript(@"C:\test\driver.inf", "Driver", @"C:\test\install.log", true);
        var start = installer.IndexOf("    $pnpOutput =", StringComparison.Ordinal);
        // Substitui todas as operações do Windows; executa o fluxo real até o resultado final.
        var script = "$ProgressPreference = 'SilentlyContinue'\n"
            + $"function Invoke-FakePnputil {{ $global:LASTEXITCODE = 259; '{ExistingPackageOutput}' }}\n"
            + "function Stop-Transcript {}\nfunction Add-PrinterDriver { Write-Output 'TEST>> registration attempted' }\n"
            + (registered ? "function Get-PrinterDriver { [PSCustomObject]@{ Name = 'Driver' } }\n" : "function Get-PrinterDriver {}\n")
            + "function Get-WindowsDriver {}\nfunction Test-Path { $false }\n"
            + "function Start-Process { [PSCustomObject]@{ ExitCode = 0 } }\n"
            + "$driverName = 'Driver'\n$inf = 'C:\\test\\driver.inf'\ntry {\n"
            + installer[start..].Replace("& pnputil.exe", "Invoke-FakePnputil", StringComparison.Ordinal);
        var (exitCode, output) = await RunScriptAsync(script);
        Assert.Contains("TEST>> registration attempted", output);
        Assert.Equal(registered ? 0 : 1, exitCode);
        Assert.Equal(registered ? "RESULT>> OK" : "RESULT>> FAIL driver not registered",
            WmiPrinterOperationsCore.ExtractResultLine(output));
    }

    private static async Task<(int ExitCode, string Output)> RunScriptAsync(string script)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var result = await stdout;
        Assert.True(string.IsNullOrWhiteSpace(await stderr), await stderr);
        return (process.ExitCode, result);
    }
}
