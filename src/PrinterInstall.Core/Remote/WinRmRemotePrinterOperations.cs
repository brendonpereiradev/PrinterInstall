using System.Globalization;
using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public sealed class WinRmRemotePrinterOperations : IRemotePrinterOperations
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PnputilTimeout = TimeSpan.FromMinutes(10);

    private readonly IPowerShellInvoker _invoker;
    private readonly IRemoteDriverFileStager _stager;

    public WinRmRemotePrinterOperations(IPowerShellInvoker invoker, IRemoteDriverFileStager stager)
    {
        _invoker = invoker;
        _stager = stager;
    }

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        const string inner = "Get-PrinterDriver | Select-Object -ExpandProperty Name";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-PrinterPort -Name '{Escape(portName)}' -PrinterHostAddress '{Escape(printerHostAddress)}' -PortNumber {portNumber}";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-Printer -Name '{Escape(printerName)}' -DriverName '{Escape(driverName)}' -PortName '{Escape(portName)}'";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
    {
        var inner = $"""
Import-Module PrintManagement -ErrorAction Stop
Print-TestPage -PrinterName '{Escape(printerQueueName)}'
""";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public async Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        const string inner = """
$printers = Get-Printer | Select-Object Name, PortName
@($printers) | ConvertTo-Json -Compress -Depth 4
""";
        var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken).ConfigureAwait(false);
        var json = RemotePrinterQueueInfoJsonParser.NormalizeInvokerLinesToJson(result) ?? string.Empty;
        var parsed = RemotePrinterQueueInfoJsonParser.Parse(json);
        return parsed.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
    }

    public Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$p = Get-Printer -Name '{Escape(printerName)}' -ErrorAction SilentlyContinue
if ($null -ne $p) {{ Remove-Printer -Name '{Escape(printerName)}' -Confirm:$false }}
";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
Import-Module PrintManagement -ErrorAction Stop
$null = Get-Printer -Name '{Escape(currentName)}' -ErrorAction Stop
Rename-Printer -Name '{Escape(currentName)}' -NewName '{Escape(newName)}' -ErrorAction Stop
";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public async Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$c = @(Get-Printer | Where-Object {{ $_.PortName -eq '{Escape(portName)}' }}).Count
$c.ToString()
";
        var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken).ConfigureAwait(false);
        if (result.Count == 0)
            return 0;
        return int.Parse(result[^1].Trim(), CultureInfo.InvariantCulture);
    }

    public Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $@"
$port = Get-PrinterPort -Name '{Escape(portName)}' -ErrorAction SilentlyContinue
if ($null -ne $port) {{ Remove-PrinterPort -Name '{Escape(portName)}' -Confirm:$false }}
";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
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
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCts.CancelAfter(PnputilTimeout);

            var infLocal = paths.LocalInfPath(package.InfFileName);
            log?.Report($"Running pnputil on {computerName} for {package.InfFileName}...");

            // Keep this script in sync with CimRemotePrinterOperations.BuildInstallerScript.
            // No rundll32 printui.dll fallback: it pops invisible modal dialogs and hangs.
            // Never set $ErrorActionPreference='Stop' at the top level, or partial
            // output (PNPUTIL>>) is lost when Add-PrinterDriver throws.
            var infFileName = System.IO.Path.GetFileName(infLocal);
            var script = $@"
$inf = '{Escape(infLocal)}'
$driverName = '{Escape(package.ExpectedDriverName)}'
$infFileName = '{Escape(infFileName)}'
$stagingRoot = Split-Path -Parent $inf

# --- Step 0: trust driver publisher (pnputil needs the catalog signer in TrustedPublisher) ---
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
                if ($elem.Certificate.Thumbprint -ne $signer.Thumbprint) {{ $toImport.Add($elem.Certificate) }}
            }}
            foreach ($storeName in @('TrustedPublisher','Root')) {{
                $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
                try {{
                    $store.Open('ReadWrite')
                    foreach ($c in $toImport) {{ try {{ $store.Add($c) }} catch {{}} }}
                }} finally {{ $store.Close() }}
            }}
            Write-Output ('TRUST>> Imported ' + $toImport.Count + ' cert(s) from ' + $catFile.Name + ' (Signer: ' + $signer.Subject + ')')
        }}
        else {{
            Write-Output ('TRUST>> No signer certificate found in ' + $catFile.Name + ' (Status: ' + $sig.Status + ')')
        }}
    }}
    catch {{ Write-Output ('TRUST>> Failed to import cert from ' + $catFile.Name + ': ' + $_.Exception.Message) }}
}}

$pnpOutput = ''
try {{ $pnpOutput = & pnputil.exe /add-driver $inf /install 2>&1 | Out-String }}
catch {{ $pnpOutput = 'EXCEPTION: ' + $_.Exception.Message }}
$pnpTrimmed = $pnpOutput.Trim()
if ($pnpTrimmed) {{ Write-Output ('PNPUTIL>> ' + $pnpTrimmed) }}

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

$addErr = $null
$success = $false
foreach ($c in $candidates) {{
    Write-Output ('SPOOLER>> Trying Add-PrinterDriver -InfPath ' + $c)
    try {{
        Add-PrinterDriver -Name $driverName -InfPath $c -ErrorAction Stop
        $verify = Get-PrinterDriver -Name $driverName -ErrorAction SilentlyContinue
        if ($verify) {{
            Write-Output ('SPOOLER>> Add-PrinterDriver OK via ' + $c)
            $success = $true
            break
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

if (-not $success) {{
    $msg = 'Driver ' + $driverName + ' not registered on target.'
    if ($addErr) {{ $msg += ' Last Add-PrinterDriver error: ' + $addErr }}
    throw $msg
}}
";
            var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, script, runCts.Token).ConfigureAwait(false);
            foreach (var line in result)
            {
                var trimmed = line?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    log?.Report(trimmed);
            }
        }
        finally
        {
            await _stager.CleanupAsync(computerName, credential, paths, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
