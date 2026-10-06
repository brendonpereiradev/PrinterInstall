using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Logging;

public interface ILocalDiagnosticCollector
{
    Task<string> CollectAsync(CancellationToken cancellationToken);
}

/// <summary>Somente consulta o Windows local; não altera serviços, políticas ou impressoras.</summary>
public sealed class LocalDiagnosticCollector : ILocalDiagnosticCollector
{
    internal const string WindowsSnapshotScript = """
        $ErrorActionPreference = 'Stop'
        function Read-Section($title, [scriptblock]$read) {
            Write-Output ('--- ' + $title + ' ---')
            try { & $read | Format-List | Out-String -Width 240 | Write-Output }
            catch { Write-Output ('Consulta indisponivel: ' + $_.Exception.ToString()) }
        }
        Read-Section 'Windows e dominio' {
            Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 3 | Select-Object Name, Domain, PartOfDomain, UserName
            Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 3 | Select-Object Caption, Version, BuildNumber, OSArchitecture
        }
        Read-Section 'Spooler' { Get-Service Spooler | Select-Object Name, Status, StartType }
        Read-Section 'Administradores locais (inclui grupos; nao expande membros do dominio)' {
            Get-LocalGroupMember -SID 'S-1-5-32-544' | Select-Object Name, SID, ObjectClass, PrincipalSource
        }
        Read-Section 'Compartilhamento administrativo local' { Get-SmbShare -Name 'ADMIN$' | Select-Object Name, Path }
        Read-Section 'Drivers instalados' {
            Get-CimInstance Win32_PrinterDriver -OperationTimeoutSec 3 | Select-Object Name, InfName, DriverPath
        }
        Read-Section 'Filas existentes' {
            Get-CimInstance Win32_Printer -OperationTimeoutSec 3 | Select-Object Name, DriverName, PortName, PrinterStatus
        }
        """;

    public async Task<string> CollectAsync(CancellationToken cancellationToken)
    {
        var basic = CaptureBasicContext();
        var command = "-NoProfile -NonInteractive -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsSnapshotScript));
        try
        {
            var result = await LocalProcessRunner.RunExecutableWithOutputAsync(
                "powershell.exe", command, TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
            return basic + "\n" + result.StandardOutput + "\n" + result.StandardError +
                $"\nColeta Windows: exit={result.Result.ReturnValue}; timeout={result.Result.TimedOut}.\n";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return basic + "\nFalha na coleta Windows: " + DiagnosticLogFormatter.FormatExceptionDetails(ex); }
    }

    public static string CaptureBasicContext()
    {
        var text = new StringBuilder();
        text.AppendLine($"Capturado em: {DateTimeOffset.Now:O}");
        text.AppendLine($"Versão do aplicativo: {Assembly.GetEntryAssembly()?.GetName().Version}");
        text.AppendLine($"Versão do Core: {typeof(LocalDiagnosticCollector).Assembly.GetName().Version}");
        try
        {
            if (Environment.ProcessPath is { } processPath)
                text.AppendLine($"Executável: {processPath}; última alteração (UTC): {File.GetLastWriteTimeUtc(processPath):O}");
        }
        catch (Exception ex) { text.AppendLine("Executável: " + DiagnosticLogFormatter.FormatException(ex)); }
        text.AppendLine($"Máquina: {Environment.MachineName}; arquitetura do processo: {(Environment.Is64BitProcess ? "x64" : "x86")}; .NET: {Environment.Version}");
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            text.AppendLine($"Usuário do processo: {identity.Name}; SID: {identity.User}");
            text.AppendLine($"Token elevado: {new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}");
            text.AppendLine($"Grupos no token (SID): {string.Join(", ", identity.Groups?.Select(g => g.Value) ?? Array.Empty<string>())}");
        }
        catch (Exception ex) { text.AppendLine("Token: " + DiagnosticLogFormatter.FormatException(ex)); }
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var ip = nic.GetIPProperties();
                text.AppendLine($"Rede: {nic.Name}; sufixo DNS: {ip.DnsSuffix}; endereços: {string.Join(", ", ip.UnicastAddresses.Select(a => a.Address))}; DNS: {string.Join(", ", ip.DnsAddresses)}; gateway: {string.Join(", ", ip.GatewayAddresses.Select(g => g.Address))}");
            }
        }
        catch (Exception ex) { text.AppendLine("Rede: " + DiagnosticLogFormatter.FormatException(ex)); }
        return text.ToString();
    }
}
