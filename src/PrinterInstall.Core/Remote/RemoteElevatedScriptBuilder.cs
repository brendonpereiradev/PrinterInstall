using System.Globalization;
using PrinterInstall.Core.Gainscha;



namespace PrinterInstall.Core.Remote;



public static class RemoteElevatedScriptBuilder

{

    public static string WrapWithResultHandling(string innerScriptBody) =>

$@"$ErrorActionPreference = 'Stop'

try {{

{innerScriptBody}

    Write-Output 'RESULT>> OK'

    exit 0

}} catch {{

    Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)

    exit 1

}}";



    public static string BuildCreateTcpPortScript(string portName, string hostAddress, int portNumber, string protocol)

    {

        var port = EscapePs(portName);

        var host = EscapePs(hostAddress);

        var body = protocol.Equals("LPR", StringComparison.OrdinalIgnoreCase)

            ? $@"

    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} -PortMonitor 'LPR Port Monitor' | Out-Null"

            : $@"

    if (Get-PrinterPort -Name '{port}' -ErrorAction SilentlyContinue) {{ return }}

    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} | Out-Null";

        return WrapWithResultHandling(body);

    }



    public static string BuildAddPrinterScript(string printerName, string driverName, string portName)

    {

        var n = EscapePs(printerName);

        var d = EscapePs(driverName);

        var p = EscapePs(portName);

        var body = $@"

    Import-Module PrintManagement -ErrorAction Stop

    if (Get-Printer -Name '{n}' -ErrorAction SilentlyContinue) {{ return }}

    Add-Printer -Name '{n}' -DriverName '{d}' -PortName '{p}' -ErrorAction Stop | Out-Null";

        return WrapWithResultHandling(body);

    }



    public static string BuildRemovePrinterScript(string printerName)

    {

        var n = EscapePs(printerName);

        var body = $@"

    Import-Module PrintManagement -ErrorAction Stop

    Remove-Printer -Name '{n}' -ErrorAction Stop | Out-Null";

        return WrapWithResultHandling(body);

    }



    public static string BuildRemoveTcpPortScript(string portName)

    {

        var p = EscapePs(portName);

        var body = $@"

    Import-Module PrintManagement -ErrorAction Stop

    if (Get-PrinterPort -Name '{p}' -ErrorAction SilentlyContinue) {{

        Remove-PrinterPort -Name '{p}' -ErrorAction Stop | Out-Null

    }}";

        return WrapWithResultHandling(body);

    }



    public static string BuildPrintTestPageScript(string printerQueueName)

    {

        var n = EscapePs(printerQueueName);

        var body = $@"

    Import-Module PrintManagement -ErrorAction Stop

    Print-TestPage -PrinterName '{n}' -ErrorAction Stop | Out-Null";

        return WrapWithResultHandling(body);

    }



    public static string BuildApplyGainschaLabelPresetScript(

        string printerQueueName,

        string templateSdsPathOnTarget,

        string cleanupSdsPathOnTarget,

        string defaultsSdsPathOnTarget,

        string deployUserName,

        int expectedWidthMm,

        int expectedHeightMm,

        string expectedStockName)

    {

        var printer = EscapePs(printerQueueName);

        var template = EscapePs(templateSdsPathOnTarget);

        var cleanup = EscapePs(cleanupSdsPathOnTarget);

        var defaults = EscapePs(defaultsSdsPathOnTarget);

        var deployUser = EscapePs(deployUserName);

        var stockName = EscapePs(expectedStockName);

        var validateFunctions = GainschaPrintingDefaultsSyncScriptFragment.BuildValidateFunctions(
            expectedWidthMm,
            expectedHeightMm);

        var interactiveSync = GainschaPrintingDefaultsInteractiveSyncScriptFragment.BuildInteractiveSyncFunction();

        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    $null = Get-Printer -Name '{printer}' -ErrorAction Stop

{validateFunctions}

    function Find-Ssdal {{
        $roots = @(
            (Join-Path -Path $env:ProgramFiles -ChildPath 'Seagull')
            (Join-Path -Path $env:ProgramFiles -ChildPath 'Seagull Scientific')
            (Join-Path -Path ${{env:ProgramFiles(x86)}} -ChildPath 'Seagull')
            (Join-Path -Path ${{env:ProgramFiles(x86)}} -ChildPath 'Seagull Scientific')
        )
        foreach ($root in $roots) {{
            if (-not (Test-Path $root)) {{ continue }}
            $hit = Get-ChildItem -Path $root -Filter ssdal.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) {{ return $hit.FullName }}
        }}
        throw 'Seagull ssdal.exe not found on target machine.'
    }}

    function Read-SdsFile {{
        param([string]$Path)
        $utf8 = New-Object System.Text.UTF8Encoding $false
        return [System.IO.File]::ReadAllText($Path, $utf8)
    }}

    function Get-UserFormDimensionsMmFromContent {{
        param([string]$Content)
        $patterns = @(
            '(?s)""User Form: Data""=hex:((?:[0-9a-fA-F]{{2}},|\s)+)',
            '(?s)User Form: Data=hex:((?:[0-9a-fA-F]{{2}},|\s)+)'
        )
        foreach ($pattern in $patterns) {{
            $match = [regex]::Match($Content, $pattern)
            if (-not $match.Success) {{ continue }}
            $hex = $match.Groups[1].Value.Replace('\', '')
            $tokens = @($hex.Split(',') | ForEach-Object {{ $_.Trim() }} | Where-Object {{ $_ }})
            if ($tokens.Count -lt 8) {{ continue }}
            $bytes = @()
            for ($i = 0; $i -lt 8; $i++) {{ $bytes += [Convert]::ToByte($tokens[$i], 16) }}
            $width = [BitConverter]::ToUInt32($bytes, 0) / 1000
            $height = [BitConverter]::ToUInt32($bytes, 4) / 1000
            return [PSCustomObject]@{{ WidthMm = [int]$width; HeightMm = [int]$height }}
        }}
        return $null
    }}

    function Invoke-SsdalSettings {{
        param(
            [string]$SsdalPath,
            [string]$PrinterName,
            [ValidateSet('import', 'export')]
            [string]$Action,
            [string]$FilePath
        )
        & $SsdalPath @('/p', $PrinterName, '/q', 'settings', $Action, $FilePath)
        if ($LASTEXITCODE -ne 0) {{ throw ""ssdal settings $Action failed with exit code $LASTEXITCODE"" }}
    }}

    $ssdal = Find-Ssdal
    $printerName = '{printer}'
    $templatePath = '{template}'
    $cleanupPath = '{cleanup}'
    $defaultsPath = '{defaults}'
    $deployUserName = '{deployUser}'
    $expectedWidthMm = {expectedWidthMm.ToString(CultureInfo.InvariantCulture)}
    $expectedHeightMm = {expectedHeightMm.ToString(CultureInfo.InvariantCulture)}
    $expectedStockName = '{stockName}'

    $exportPath = Join-Path -Path $env:TEMP -ChildPath ""PrinterInstall-gainscha-verify-$([Guid]::NewGuid().ToString('N')).sds""

    try {{
        if (-not (Test-Path -LiteralPath $templatePath)) {{ throw ""Template SDS not found: $templatePath"" }}

        Write-Output 'STEP>> ssdal import template'
        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $templatePath

        if (Test-Path -LiteralPath $cleanupPath) {{
            Write-Output 'STEP>> ssdal import cleanup'
            Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $cleanupPath
        }}

        $printerRegPath = ""HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\$printerName""
        $driverDataPath = Join-Path -Path $printerRegPath -ChildPath 'PrinterDriverData'

        if (Test-Path -LiteralPath $driverDataPath) {{
            Write-Output 'STEP>> writing PrinterDriverData user form registry keys'
            $wMicro = [uint32]($expectedWidthMm * 1000)
            $hMicro = [uint32]($expectedHeightMm * 1000)
            $wBytes = [BitConverter]::GetBytes($wMicro)
            $hBytes = [BitConverter]::GetBytes($hMicro)
            $formData = [byte[]]@($wBytes[0], $wBytes[1], $wBytes[2], $wBytes[3], $hBytes[0], $hBytes[1], $hBytes[2], $hBytes[3], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1)

            Set-ItemProperty -LiteralPath $driverDataPath -Name 'User Form: Data' -Value $formData -Type Binary -Force
            Set-ItemProperty -LiteralPath $driverDataPath -Name 'User Form: Name' -Value 'USER' -Type String -Force
            Set-ItemProperty -LiteralPath $driverDataPath -Name 'User Form: Label Stock Type' -Value 0 -Type DWord -Force
            Set-ItemProperty -LiteralPath $driverDataPath -Name 'User Form: Variable Bottom Margin' -Value 0x0000319c -Type DWord -Force
            Set-ItemProperty -LiteralPath $driverDataPath -Name 'User Form: Variable Top Margin' -Value 0x0000319c -Type DWord -Force
            Set-ItemProperty -LiteralPath $driverDataPath -Name 'Disable Shared PaperSize' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue

            Get-ItemProperty -LiteralPath $driverDataPath -ErrorAction SilentlyContinue | Get-Member -MemberType NoteProperty | ForEach-Object {{
                $prop = $_.Name
                if ($prop -match '(?i)(^Stock:\s*(2\s*x\s*4|4\s*x\s*4|4\s*x\s*6)|2\s*x\s*4|4\s*x\s*4|4\s*x\s*6)') {{
                    Remove-ItemProperty -LiteralPath $driverDataPath -Name $prop -Force -ErrorAction SilentlyContinue
                }}
            }}
        }}

        $sdsRoots = @(
            (Join-Path -Path $env:SystemRoot -ChildPath 'System32\spool\drivers')
            (Join-Path -Path $env:ProgramFiles -ChildPath 'Seagull')
            (Join-Path -Path ${{env:ProgramFiles(x86)}} -ChildPath 'Seagull')
        )
        foreach ($sRoot in $sdsRoots) {{
            if (-not (Test-Path -LiteralPath $sRoot)) {{ continue }}
            Get-ChildItem -Path $sRoot -Filter 'Defaults[GN]*.sds' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {{
                try {{
                    $sContent = [System.IO.File]::ReadAllText($_.FullName)
                    $sClean = [System.Text.RegularExpressions.Regex]::Replace($sContent, '(?is)<stock>\s*Name=(2\s*x\s*4|4\s*x\s*4|4\s*x\s*6)\s*.*?</stock>', '')
                    if ($sClean -ne $sContent) {{
                        [System.IO.File]::WriteAllText($_.FullName, $sClean, [System.Text.UTF8Encoding]::new($false))
                    }}
                }} catch {{ }}
            }}
        }}

        if (Test-Path -LiteralPath $printerRegPath) {{
            $devMode = (Get-ItemProperty -LiteralPath $printerRegPath -Name 'Default DevMode' -ErrorAction SilentlyContinue).'Default DevMode'
            if ($null -ne $devMode -and $devMode.Length -ge 166) {{
                Write-Output 'STEP>> updating Default DevMode in registry'
                $dmPaperSize = [BitConverter]::GetBytes([int16]256)
                $dmPaperLength = [BitConverter]::GetBytes([int16]($expectedHeightMm * 10))
                $dmPaperWidth = [BitConverter]::GetBytes([int16]($expectedWidthMm * 10))

                [Array]::Copy($dmPaperSize, 0, $devMode, 78, 2)
                [Array]::Copy($dmPaperLength, 0, $devMode, 80, 2)
                [Array]::Copy($dmPaperWidth, 0, $devMode, 82, 2)

                $formNameBytes = [System.Text.Encoding]::Unicode.GetBytes(""USER`0"")
                for ($i = 0; $i -lt 64; $i++) {{ $devMode[102 + $i] = 0 }}
                [Array]::Copy($formNameBytes, 0, $devMode, 102, [Math]::Min($formNameBytes.Length, 64))

                Set-ItemProperty -LiteralPath $printerRegPath -Name 'Default DevMode' -Value $devMode -Type Binary -Force
            }}
        }}

        Write-Output 'STEP>> purging cached user DevModes2'
        Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue | ForEach-Object {{
            $userDevModes = Join-Path -Path $_.PSPath -ChildPath 'Printers\DevModes2'
            if (Test-Path -LiteralPath $userDevModes) {{
                Remove-ItemProperty -LiteralPath $userDevModes -Name $printerName -Force -ErrorAction SilentlyContinue
            }}
        }}

        Write-Output 'STEP>> restarting Spooler to refresh memory cache'
        Restart-Service -Name Spooler -Force

        Write-Output 'STEP>> ssdal export preferences'
        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action export -FilePath $exportPath

        Write-Output 'STEP>> validate preferences export'
        $exportContent = Read-SdsFile -Path $exportPath
        if ($exportContent -notmatch [regex]::Escape(""Name=$expectedStockName"")) {{
            throw ""Stock esperado '$expectedStockName' nao encontrado no export apos importar preferencias.""
        }}
        $actual = Get-UserFormDimensionsMmFromContent -Content $exportContent
        if ($null -eq $actual) {{
            throw 'Export SDS apos importar preferencias nao contem User Form: Data.'
        }}
        if ($actual.WidthMm -ne $expectedWidthMm -or $actual.HeightMm -ne $expectedHeightMm) {{
            throw ""Dimensao USER apos importar preferencias incorreta: esperado ${{expectedWidthMm}}x${{expectedHeightMm}} mm, encontrado $($actual.WidthMm)x$($actual.HeightMm) mm.""
        }}
    }} finally {{
        if (Test-Path -LiteralPath $exportPath) {{ Remove-Item -LiteralPath $exportPath -Force -ErrorAction SilentlyContinue }}
    }}";

        return WrapWithResultHandling(body);

    }



    public static string BuildRenamePrinterScript(string currentName, string newName)

    {

        var c = EscapePs(currentName);

        var n = EscapePs(newName);

        var body = $@"

    Import-Module PrintManagement -ErrorAction Stop

    $null = Get-Printer -Name '{c}' -ErrorAction Stop

    Rename-Printer -Name '{c}' -NewName '{n}' -ErrorAction Stop | Out-Null";

        return WrapWithResultHandling(body);

    }



    private static string EscapePs(string value) => value.Replace("'", "''", StringComparison.Ordinal);

}


