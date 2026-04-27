using System.Globalization;
using System.Management;
using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Implementa operações remotas via WMI/DCOM (fallback para quando WinRM não está disponível).
/// Lista drivers instalados, cria portas TCP/IP e adiciona filas de impressão.
/// </summary>
public sealed class CimRemotePrinterOperations : IRemotePrinterOperations
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(5);
    // pnputil + Add-PrinterDriver together finish in seconds on a healthy
    // target. A short timeout prevents the UI from appearing frozen when
    // something hangs on the remote side (e.g. a session-0 MessageBox).
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RenameOperationTimeout = TimeSpan.FromMinutes(2);

    private readonly IRemoteDriverFileStager _stager;
    private readonly IRemoteProcessRunner _processRunner;

    public CimRemotePrinterOperations(IRemoteDriverFileStager stager, IRemoteProcessRunner processRunner)
    {
        _stager = stager;
        _processRunner = processRunner;
    }

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            var query = new ObjectQuery("SELECT Name FROM Win32_PrinterDriver");
            using var searcher = new ManagementObjectSearcher(scope, query);

            var list = new List<string>();
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var raw = mo["Name"]?.ToString();
                    var normalized = NormalizeWmiDriverName(raw);
                    if (!string.IsNullOrEmpty(normalized))
                        list.Add(normalized);
                }
                finally
                {
                    mo.Dispose();
                }
            }

            return (IReadOnlyList<string>)list;
        }, cancellationToken);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            if (PortExists(scope, portName))
                return;

            using var portClass = new ManagementClass(scope, new ManagementPath("Win32_TCPIPPrinterPort"), null);
            using var port = portClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_TCPIPPrinterPort instance.");

            port["Name"] = portName;
            port["HostAddress"] = printerHostAddress;
            port["PortNumber"] = portNumber;
            port["Protocol"] = MapProtocol(protocol);
            port["SNMPEnabled"] = false;
            port["Queue"] = "";

            port.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken);
    }

    public Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();
            return PrinterExists(scope, printerDisplayName);
        }, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            if (PrinterExists(scope, printerName))
                return;

            using var printerClass = new ManagementClass(scope, new ManagementPath("Win32_Printer"), null);
            using var printer = printerClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_Printer instance.");

            printer["DeviceID"] = printerName;
            printer["Name"] = printerName;
            printer["DriverName"] = driverName;
            printer["PortName"] = portName;
            printer["Network"] = true;
            printer["Shared"] = false;

            printer.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken);
    }

    public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();

            var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{EscapeWql(printerQueueName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (mo)
                {
                    mo.InvokeMethod("PrintTestPage", Array.Empty<object>());
                    return;
                }
            }

            throw new InvalidOperationException($"Printer queue not found for test page: {printerQueueName}");
        }, cancellationToken);
    }

    private static ManagementScope CreateScope(string computerName, NetworkCredential credential)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Username = BuildCredentialUserName(credential),
            Password = credential.Password ?? "",
            EnablePrivileges = true
        };
        var path = $@"\\{computerName.Trim()}\root\cimv2";
        return new ManagementScope(path, options);
    }

    private static bool PortExists(ManagementScope scope, string portName)
    {
        var query = new ObjectQuery($"SELECT Name FROM Win32_TCPIPPrinterPort WHERE Name='{EscapeWql(portName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static bool PrinterExists(ManagementScope scope, string printerName)
    {
        var query = new ObjectQuery($"SELECT Name FROM Win32_Printer WHERE Name='{EscapeWql(printerName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static int MapProtocol(string protocol)
    {
        if (string.Equals(protocol, "LPR", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    /// <summary>
    /// Win32_PrinterDriver.Name vem no formato "Driver,Version,Environment"
    /// (por exemplo, "Lexmark Universal v4 XL,3,Windows x64"). Mantemos só o driver.
    /// </summary>
    private static string NormalizeWmiDriverName(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var commaIndex = raw.IndexOf(',');
        return (commaIndex >= 0 ? raw[..commaIndex] : raw).Trim();
    }

    private static string BuildCredentialUserName(NetworkCredential credential)
    {
        if (!string.IsNullOrEmpty(credential.Domain))
            return $"{credential.Domain}\\{credential.UserName}";
        return credential.UserName;
    }

    private static string EscapeWql(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string EscapePs(string s) => s.Replace("'", "''");

    public Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RemotePrinterQueueInfo>>(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();
            var query = new ObjectQuery("SELECT Name, PortName FROM Win32_Printer");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var list = new List<RemotePrinterQueueInfo>();
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (mo)
                {
                    var name = mo["Name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    var port = mo["PortName"]?.ToString();
                    list.Add(new RemotePrinterQueueInfo(name, port));
                }
            }
            return list;
        }, cancellationToken);
    }

    public Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();
            var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{EscapeWql(printerName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    mo.Delete();
                }
            }
        }, cancellationToken);
    }

    public async Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
    {
        var cmd = BuildRenamePrinterCommandLine(currentName, newName);
        var runResult = await _processRunner.RunAsync(computerName, credential, cmd, RenameOperationTimeout, cancellationToken).ConfigureAwait(false);
        if (runResult.TimedOut)
            throw new TimeoutException($"Renomear a fila em {computerName} excedeu o tempo de {RenameOperationTimeout}.");
        if (runResult.ReturnValue != 0)
            throw new InvalidOperationException($"Renomear a fila em {computerName} falhou (WMI return {runResult.ReturnValue}).");
    }

    private static string BuildRenamePrinterCommandLine(string currentName, string newName)
    {
        var n = EscapePs(currentName);
        var m = EscapePs(newName);
        return string.Format(
            CultureInfo.InvariantCulture,
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Import-Module PrintManagement -ErrorAction Stop; $null = Get-Printer -Name '{0}' -ErrorAction Stop; Rename-Printer -Name '{0}' -NewName '{1}' -ErrorAction Stop\"",
            n,
            m);
    }

    public Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();
            var query = new ObjectQuery($"SELECT Name FROM Win32_Printer WHERE PortName='{EscapeWql(portName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            var count = 0;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo) { count++; }
            }
            return count;
        }, cancellationToken);
    }

    public Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(computerName, credential);
            scope.Connect();
            var query = new ObjectQuery($"SELECT * FROM Win32_TCPIPPrinterPort WHERE Name='{EscapeWql(portName)}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    mo.Delete();
                }
            }
        }, cancellationToken);
    }

    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(StageTimeout);

        RemoteDriverStagingPaths paths;
        try
        {
            log?.Report($"Staging driver files on \\\\{computerName}\\ADMIN$...");
            paths = await _stager.StageAsync(computerName, credential, package.RootFolder, stageCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to stage driver files to {computerName}: {ex.Message}", ex);
        }

        try
        {
            var infLocal = paths.LocalInfPath(package.InfFileName);
            var installLogLocal = paths.LocalLogPath("install.log");
            var installScriptLocal = paths.LocalInfPath("install.ps1");

            // Write a self-contained installer script to the staged folder and
            // invoke it via powershell.exe -File. The script uses Start-Transcript
            // to log everything to install.log itself, so we do NOT need a
            // cmd.exe wrapper or shell redirection. Calling powershell.exe
            // directly keeps the Win32_Process.Create command line free of
            // nested quotes (which cmd.exe parses unpredictably and caused
            // install.log to come back empty, i.e. "no RESULT line").
            var scriptContent = BuildInstallerScript(infLocal, package.ExpectedDriverName, installLogLocal);
            await _stager.WriteTextFileAsync(computerName, credential, paths, "install.ps1", scriptContent, cancellationToken).ConfigureAwait(false);

            var runCmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{installScriptLocal}\"";

            log?.Report($"Launching install script on {computerName} via WMI (timeout {InstallTimeout.TotalMinutes:F0}min)...");
            var runResult = await _processRunner.RunAsync(computerName, credential, runCmd, InstallTimeout, cancellationToken).ConfigureAwait(false);
            var installOutput = await _stager.ReadLogAsync(computerName, credential, paths, "install.log", cancellationToken).ConfigureAwait(false);

            foreach (var line in SplitLines(installOutput))
                log?.Report(line);

            if (runResult.ReturnValue != 0)
                throw new InvalidOperationException($"Install script could not start on {computerName} (WMI return {runResult.ReturnValue}).");
            if (runResult.TimedOut)
                throw new TimeoutException($"Install script timed out on {computerName} after {InstallTimeout}. Remote process was killed.");

            var resultLine = ExtractResultLine(installOutput);
            if (!string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
            {
                var detail = string.IsNullOrEmpty(resultLine) ? "no RESULT line" : resultLine;
                throw new InvalidOperationException($"Add-PrinterDriver failed on {computerName}: {detail}");
            }
        }
        finally
        {
            await _stager.CleanupAsync(computerName, credential, paths, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string BuildInstallerScript(string infLocal, string driverName, string logPath)
    {
        // The script logs everything itself via Start-Transcript so the caller
        // doesn't have to redirect with cmd.exe (nested quotes break Win32_Process.Create).
        //
        // No rundll32 printui.dll fallback here: running under Win32_Process.Create
        // the script executes as SYSTEM in session 0, where printui.dll pops an
        // invisible MessageBox on any error and the process hangs indefinitely.
        //
        // Instead we try Add-PrinterDriver with several candidate InfPath values,
        // in preference order:
        //   1) %windir%\INF\oemXX.inf (pnputil's published copy)
        //   2) DriverStore\FileRepository\<pkg>\Gprinter.inf (files co-located;
        //      this is the format AddPrinterDriverEx accepts most reliably for
        //      v3 package-aware printer drivers such as the Gprinter one)
        //   3) the original staged INF (last resort)
        //
        // IMPORTANT: never set $ErrorActionPreference='Stop' at the top level,
        // otherwise pnputil output is lost when Add-PrinterDriver throws.
        var infFileName = System.IO.Path.GetFileName(infLocal);
        return
$@"Start-Transcript -Path '{EscapePs(logPath)}' -Force | Out-Null
try {{
    $inf = '{EscapePs(infLocal)}'
    $driverName = '{EscapePs(driverName)}'
    $infFileName = '{EscapePs(infFileName)}'
    $stagingRoot = Split-Path -Parent $inf

    # --- Step 0: trust the driver publisher certificate chain ---
    # pnputil /add-driver refuses to stage a package whose catalog signer
    # is not already in LocalMachine\TrustedPublisher (error: 'O fornecedor
    # de um catalogo assinado Authenticode ainda nao foi considerado
    # confiavel'). We extract the cert chain from each .cat file under the
    # staging folder and import it into TrustedPublisher + Root so pnputil
    # accepts the package. Running under SYSTEM via Win32_Process.Create
    # gives us the rights to touch LocalMachine stores.
    $catFiles = @(Get-ChildItem -LiteralPath $stagingRoot -Filter '*.cat' -ErrorAction SilentlyContinue)
    foreach ($catFile in $catFiles) {{
        try {{
            $sig = Get-AuthenticodeSignature -FilePath $catFile.FullName
            $signer = $sig.SignerCertificate
            if ($signer) {{
                $toImport = New-Object System.Collections.Generic.List[System.Security.Cryptography.X509Certificates.X509Certificate2]
                $toImport.Add($signer)
                $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
                $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
                [void]$chain.Build($signer)
                foreach ($elem in $chain.ChainElements) {{
                    if ($elem.Certificate.Thumbprint -ne $signer.Thumbprint) {{
                        $toImport.Add($elem.Certificate)
                    }}
                }}
                foreach ($storeName in @('TrustedPublisher','Root')) {{
                    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
                    try {{
                        $store.Open('ReadWrite')
                        foreach ($c in $toImport) {{
                            try {{ $store.Add($c) }} catch {{}}
                        }}
                    }} finally {{ $store.Close() }}
                }}
                Write-Output ('TRUST>> Imported ' + $toImport.Count + ' cert(s) from ' + $catFile.Name + ' (Signer: ' + $signer.Subject + ')')
            }}
            else {{
                Write-Output ('TRUST>> No signer certificate found in ' + $catFile.Name + ' (Status: ' + $sig.Status + ')')
            }}
        }}
        catch {{
            Write-Output ('TRUST>> Failed to import cert from ' + $catFile.Name + ': ' + $_.Exception.Message)
        }}
    }}

    # --- Step 1: publish driver package into the Driver Store ---
    $pnpOutput = ''
    try {{ $pnpOutput = & pnputil.exe /add-driver $inf /install 2>&1 | Out-String }}
    catch {{ $pnpOutput = 'EXCEPTION: ' + $_.Exception.Message }}
    $pnpTrimmed = $pnpOutput.Trim()
    if ($pnpTrimmed) {{ Write-Output ('PNPUTIL>> ' + $pnpTrimmed) }}

    # --- Step 2: build candidate InfPath list ---
    $candidates = New-Object System.Collections.Generic.List[string]

    $oemMatch = [regex]::Match($pnpOutput, 'oem\d+\.inf', 'IgnoreCase')
    if ($oemMatch.Success) {{
        $oemPath = Join-Path $env:windir ('INF\' + $oemMatch.Value)
        if (Test-Path -LiteralPath $oemPath) {{ $candidates.Add($oemPath) }}
    }}

    try {{
        $wd = Get-WindowsDriver -Online -ErrorAction SilentlyContinue |
              Where-Object {{ $_.OriginalFileName -like ('*\' + $infFileName) }} |
              Select-Object -First 1
        if ($wd -and $wd.OriginalFileName -and (Test-Path -LiteralPath $wd.OriginalFileName)) {{
            if (-not $candidates.Contains($wd.OriginalFileName)) {{ $candidates.Add($wd.OriginalFileName) }}
        }}
    }} catch {{}}

    if (-not $candidates.Contains($inf)) {{ $candidates.Add($inf) }}

    # --- Step 3: try each candidate, bail out on the first that sticks ---
    $addErr = $null
    foreach ($c in $candidates) {{
        Write-Output ('SPOOLER>> Trying Add-PrinterDriver -InfPath ' + $c)
        try {{
            Add-PrinterDriver -Name $driverName -InfPath $c -ErrorAction Stop
            $verify = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
            if ($verify) {{
                Write-Output ('SPOOLER>> Add-PrinterDriver OK via ' + $c)
                Write-Output 'RESULT>> OK'
                Stop-Transcript | Out-Null
                exit 0
            }}
            else {{
                Write-Output ('SPOOLER>> cmdlet succeeded but driver not visible via ' + $c)
            }}
        }}
        catch {{
            $addErr = $_.Exception.Message
            Write-Output ('SPOOLER>> Failed via ' + $c + ': ' + $addErr)
        }}
    }}

    $detail = 'driver not registered'
    if ($addErr) {{ $detail = $addErr }}
    Write-Output ('RESULT>> FAIL ' + $detail)
    Stop-Transcript | Out-Null
    exit 1
}}
catch {{
    Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)
    try {{ Stop-Transcript | Out-Null }} catch {{}}
    exit 1
}}
";
    }

    private static IEnumerable<string> SplitLines(string s)
    {
        if (string.IsNullOrEmpty(s))
            yield break;
        foreach (var raw in s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var t = raw.Trim();
            if (!string.IsNullOrEmpty(t))
                yield return t;
        }
    }

    private static string ExtractResultLine(string s)
    {
        foreach (var line in SplitLines(s))
            if (line.StartsWith("RESULT>> ", StringComparison.Ordinal))
                return line;
        return string.Empty;
    }
}
