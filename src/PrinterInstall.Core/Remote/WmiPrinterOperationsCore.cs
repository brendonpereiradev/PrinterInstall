using System.Globalization;
using System.Management;
using System.Net;
using PrinterInstall.Core.Drivers;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Helpers WMI partilhados entre operações locais e remotas.
/// </summary>
public static class WmiPrinterOperationsCore
{
    public static ManagementScope CreateLocalScope()
    {
        var scope = new ManagementScope(@"root\cimv2");
        scope.Connect();
        return scope;
    }

    public static ManagementScope CreateRemoteScope(string computerName, NetworkCredential credential)
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

    public static bool PortExists(ManagementScope scope, string portName)
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

    public static bool PrinterExists(ManagementScope scope, string printerName)
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

    public static int MapProtocol(string protocol)
    {
        if (string.Equals(protocol, "LPR", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    public static string NormalizeWmiDriverName(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var commaIndex = raw.IndexOf(',');
        return (commaIndex >= 0 ? raw[..commaIndex] : raw).Trim();
    }

    public static string BuildCredentialUserName(NetworkCredential credential)
    {
        if (!string.IsNullOrEmpty(credential.Domain))
            return $"{credential.Domain}\\{credential.UserName}";
        return credential.UserName;
    }

    public static string EscapeWql(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    public static string EscapePs(string s) => s.Replace("'", "''");

    public static string BuildRunAsRelaunchBlock() =>
$@"$scriptPath = $MyInvocation.MyCommand.Path
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {{
    Write-Output 'ELEVATE>> Solicitando privilegios de administrador (UAC)...'
    try {{
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath
        )
        if ($null -eq $proc) {{
            Write-Output 'RESULT>> FAIL Prompt UAC cancelado. Execute o Printer Install como administrador.'
            exit 1
        }}
        exit $proc.ExitCode
    }}
    catch {{
        Write-Output ('ELEVATE>> UAC negado: ' + $_.Exception.Message)
        Write-Output 'RESULT>> FAIL Execute o Printer Install como administrador ou aceite o prompt UAC.'
        exit 1
    }}
}}

";

    public static string BuildLocalElevatedScript(string logPath, string innerScriptBody) =>
        BuildRunAsRelaunchBlock() +
$@"$logPath = '{EscapePs(logPath)}'
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path -LiteralPath $logDir)) {{
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
}}
Start-Transcript -LiteralPath $logPath -Force | Out-Null
try {{
{innerScriptBody}
}} finally {{
    Stop-Transcript | Out-Null
}}";

    public static string BuildRenamePrinterCommandLine(string currentName, string newName)
    {
        var n = EscapePs(currentName);
        var m = EscapePs(newName);
        return string.Format(
            CultureInfo.InvariantCulture,
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Import-Module PrintManagement -ErrorAction Stop; $null = Get-Printer -Name '{0}' -ErrorAction Stop; Rename-Printer -Name '{0}' -NewName '{1}' -ErrorAction Stop\"",
            n,
            m);
    }

    public static string BuildPrintTestPageCommandLine(string printerQueueName)
    {
        var n = EscapePs(printerQueueName);
        return string.Format(
            CultureInfo.InvariantCulture,
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Import-Module PrintManagement -ErrorAction Stop; $null = Get-Printer -Name '{0}' -ErrorAction Stop; Print-TestPage -PrinterName '{0}' -ErrorAction Stop\"",
            n);
    }

    /// <summary>
    /// Invoca Win32_Printer.PrintTestPage e valida o código de retorno WMI (0 = sucesso).
    /// </summary>
    public static void InvokeWmiPrintTestPage(ManagementObject printer)
    {
        using var outParams = printer.InvokeMethod("PrintTestPage", null, null)
            ?? throw new InvalidOperationException("PrintTestPage returned no output.");
        var returnValue = Convert.ToUInt32(outParams["ReturnValue"], CultureInfo.InvariantCulture);
        if (returnValue != 0)
            throw new InvalidOperationException(DescribePrintTestPageWmiError(returnValue));
    }

    private static string DescribePrintTestPageWmiError(uint returnValue) => returnValue switch
    {
        5 => "PrintTestPage failed: Access denied (WMI return 5). Run the app as administrator.",
        _ => $"PrintTestPage failed with WMI return code {returnValue}."
    };

    public static string BuildPrintTestPageRundll32CommandLine(string printerQueueName)
    {
        var escaped = printerQueueName.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"rundll32.exe printui.dll,PrintUIEntry /k /n \"{escaped}\"";
    }

    public static void PrintTestPageOnScope(ManagementScope scope, string printerQueueName, CancellationToken cancellationToken)
    {
        var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{EscapeWql(printerQueueName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);

        foreach (ManagementObject mo in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (mo)
            {
                InvokeWmiPrintTestPage(mo);
                return;
            }
        }

        throw new InvalidOperationException($"Printer queue not found for test page: {printerQueueName}");
    }

    /// <summary>
    /// Aguarda um job aparecer na fila do spooler (confirma que a página de teste foi enfileirada).
    /// </summary>
    public static bool WaitForPrintJobOnPrinter(
        ManagementScope scope,
        string printerQueueName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var jobPrefix = printerQueueName + ",";
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasPrintJobForPrinter(scope, jobPrefix))
                return true;
            Thread.Sleep(300);
        }

        return false;
    }

    private static bool HasPrintJobForPrinter(ManagementScope scope, string jobNamePrefix)
    {
        var query = new ObjectQuery("SELECT Name FROM Win32_PrintJob");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                var name = mo["Name"]?.ToString() ?? "";
                if (name.StartsWith(jobNamePrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static string BuildInstallerScript(string infLocal, string driverName, string logPath, bool skipRunAsBlock = false)
    {
        var infFileName = Path.GetFileName(infLocal);
        var elevationBlock = skipRunAsBlock ? "" : BuildRunAsRelaunchBlock();
        return elevationBlock +
$@"Start-Transcript -Path '{EscapePs(logPath)}' -Force | Out-Null
try {{
    $inf = '{EscapePs(infLocal)}'
    $driverName = '{EscapePs(driverName)}'
    $infFileName = '{EscapePs(infFileName)}'
    $stagingRoot = Split-Path -Parent $inf

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

    $pnpOutput = & pnputil.exe /add-driver $inf /install 2>&1
    $pnpExit = $LASTEXITCODE
    $pnpOutputText = ($pnpOutput | Out-String).Trim()
    if ($pnpOutputText) {{ Write-Output ('PNPUTIL>> ' + $pnpOutputText) }}

    $pnpSuccess = ($pnpExit -eq 0) -and ($pnpOutputText -match '(?i)(Driver package added successfully|Pacote de driver adicionado|Added driver packages:\s*[1-9]|Pacotes de driver adicionados:\s*[1-9])')
    if ($pnpOutputText -match '(?i)(Failed to add|Falha ao adicionar|Access is denied|Acesso negado|Pacotes de driver adicionados:\s*0|Added driver packages:\s*0)') {{
        $pnpSuccess = $false
    }}

    if (-not $pnpSuccess) {{
        $pnpDetail = 'pnputil exit code ' + $pnpExit
        $pnpLines = @($pnpOutputText -split '\r?\n' | Where-Object {{ $_.Trim() -ne '' }})
        for ($i = $pnpLines.Count - 1; $i -ge 0; $i--) {{
            $line = $pnpLines[$i].Trim()
            if ($line -match '^(?i)(Microsoft PnP Utility|Utilitário PnP da Microsoft|Utilitario PnP da Microsoft)$') {{ continue }}
            $pnpDetail = $line
            break
        }}
        if ($pnpDetail -match '(?i)(Access is denied|Acesso negado)') {{
            $pnpDetail = $pnpDetail + ' Execute o Printer Install como administrador ou aceite o prompt UAC.'
        }}
        Write-Output ('RESULT>> FAIL pnputil: ' + $pnpDetail)
        Stop-Transcript | Out-Null
        exit 1
    }}

    function Test-DriverRegistered {{
        param([string]$Name)
        return $null -ne (Get-PrinterDriver -Name $Name -ErrorAction SilentlyContinue)
    }}

    $addErr = $null
    Write-Output ('SPOOLER>> Trying Add-PrinterDriver -Name ' + $driverName)
    try {{
        Add-PrinterDriver -Name $driverName -ErrorAction Stop
        if (Test-DriverRegistered $driverName) {{
            Write-Output 'SPOOLER>> Add-PrinterDriver OK (name only)'
            Write-Output 'RESULT>> OK'
            Stop-Transcript | Out-Null
            exit 0
        }}
        Write-Output 'SPOOLER>> cmdlet succeeded but driver not visible (name only)'
    }}
    catch {{
        $addErr = $_.Exception.Message
        Write-Output ('SPOOLER>> Name only failed: ' + $addErr)
    }}

    $candidates = New-Object System.Collections.Generic.List[string]

    try {{
        $storeDrivers = Get-WindowsDriver -Online -ErrorAction SilentlyContinue
        foreach ($wd in $storeDrivers) {{
            if (-not $wd.OriginalFileName -or -not (Test-Path -LiteralPath $wd.OriginalFileName)) {{ continue }}
            $leaf = Split-Path -Leaf $wd.OriginalFileName
            if (($leaf -ieq $infFileName) -or ($wd.Driver -eq $driverName)) {{
                if (-not $candidates.Contains($wd.OriginalFileName)) {{ $candidates.Add($wd.OriginalFileName) }}
            }}
        }}
    }} catch {{}}

    $oemMatch = [regex]::Match($pnpOutputText, 'oem\d+\.inf', 'IgnoreCase')
    if ($oemMatch.Success) {{
        $oemPath = Join-Path $env:windir ('INF\' + $oemMatch.Value)
        if ((Test-Path -LiteralPath $oemPath) -and -not $candidates.Contains($oemPath)) {{ $candidates.Add($oemPath) }}
    }}

    foreach ($c in $candidates) {{
        Write-Output ('SPOOLER>> Trying Add-PrinterDriver -InfPath ' + $c)
        try {{
            Add-PrinterDriver -Name $driverName -InfPath $c -ErrorAction Stop
            if (Test-DriverRegistered $driverName) {{
                Write-Output ('SPOOLER>> Add-PrinterDriver OK via ' + $c)
                Write-Output 'RESULT>> OK'
                Stop-Transcript | Out-Null
                exit 0
            }}
            Write-Output ('SPOOLER>> cmdlet succeeded but driver not visible via ' + $c)
        }}
        catch {{
            $addErr = $_.Exception.Message
            Write-Output ('SPOOLER>> Failed via ' + $c + ': ' + $addErr)
        }}
    }}

    $printuiInf = if ($candidates.Count -gt 0) {{ $candidates[0] }} else {{ $inf }}
    Write-Output ('SPOOLER>> Trying printui /ia -f ' + $printuiInf)
    try {{
        $printuiProc = Start-Process -FilePath 'rundll32.exe' -ArgumentList @(
            'printui.dll,PrintUIEntry', '/ia', '/m', $driverName, '/f', $printuiInf
        ) -Wait -PassThru -WindowStyle Hidden
        if ($printuiProc.ExitCode -eq 0 -and (Test-DriverRegistered $driverName)) {{
            Write-Output 'SPOOLER>> printui /ia OK'
            Write-Output 'RESULT>> OK'
            Stop-Transcript | Out-Null
            exit 0
        }}
        Write-Output ('SPOOLER>> printui /ia exit ' + $printuiProc.ExitCode)
    }}
    catch {{
        $addErr = $_.Exception.Message
        Write-Output ('SPOOLER>> printui /ia failed: ' + $addErr)
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

    public static IEnumerable<string> SplitLines(string s)
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

    public static string ExtractResultLine(string s)
    {
        foreach (var line in SplitLines(s))
            if (line.StartsWith("RESULT>> ", StringComparison.Ordinal))
                return line;
        return string.Empty;
    }

    public static string DescribeInstallScriptFailure(string installOutput, uint processReturnValue, uint? wmiCreateReturn = null)
    {
        if (wmiCreateReturn is uint wmi && wmi != 0)
            return $"Install script could not start locally (WMI return {wmi}). Run Printer Install as administrator.";

        var resultLine = ExtractResultLine(installOutput);
        if (!string.IsNullOrEmpty(resultLine))
        {
            if (resultLine.StartsWith("RESULT>> FAIL", StringComparison.Ordinal))
            {
                var detail = resultLine["RESULT>> FAIL".Length..].Trim();
                detail = EnrichPnputilFailureDetail(detail, installOutput);
                return detail;
            }

            return resultLine;
        }

        foreach (var line in SplitLines(installOutput).Reverse())
        {
            if (line.StartsWith("SPOOLER>> Failed", StringComparison.Ordinal)
                || line.StartsWith("PNPUTIL>>", StringComparison.Ordinal)
                || line.StartsWith("TRUST>> Failed", StringComparison.Ordinal))
                return line;
        }

        return processReturnValue != 0
            ? $"Install script exited with code {processReturnValue}."
            : "Install script failed with no diagnostic output.";
    }

    private static string EnrichPnputilFailureDetail(string detail, string installOutput)
    {
        if (!detail.StartsWith("pnputil:", StringComparison.OrdinalIgnoreCase))
            return detail;

        var inline = detail["pnputil:".Length..].Trim();
        if (!PnputilOutputParser.IsHeaderOnly(inline))
            return detail;

        var pnputilSection = ExtractPnputilSection(installOutput);
        if (!string.IsNullOrWhiteSpace(pnputilSection))
        {
            var extracted = PnputilOutputParser.ExtractFailureDetail(pnputilSection);
            if (!string.IsNullOrEmpty(extracted) && !PnputilOutputParser.IsHeaderOnly(extracted))
                return "pnputil: " + extracted;
        }

        return detail;
    }

    private static string? ExtractPnputilSection(string installOutput)
    {
        var sb = new System.Text.StringBuilder();
        var inSection = false;
        foreach (var line in SplitLines(installOutput))
        {
            if (line.StartsWith("PNPUTIL>> ", StringComparison.Ordinal))
            {
                inSection = true;
                sb.AppendLine(line["PNPUTIL>> ".Length..]);
                continue;
            }

            if (!inSection)
                continue;

            if (line.StartsWith("SPOOLER>>", StringComparison.Ordinal)
                || line.StartsWith("RESULT>>", StringComparison.Ordinal)
                || line.StartsWith("TRUST>>", StringComparison.Ordinal)
                || line.StartsWith("ELEVATE>>", StringComparison.Ordinal))
                break;

            sb.AppendLine(line);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
