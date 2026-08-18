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

{interactiveSync}

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

    $defaultsExportPath = Join-Path -Path $env:TEMP -ChildPath ""PrinterInstall-gainscha-defaults-$([Guid]::NewGuid().ToString('N')).sds""

    try {{

        if (-not (Test-Path -LiteralPath $templatePath)) {{ throw ""Template SDS not found: $templatePath"" }}

        if (-not (Test-Path -LiteralPath $cleanupPath)) {{ throw ""Cleanup SDS not found: $cleanupPath"" }}

        if (-not (Test-Path -LiteralPath $defaultsPath)) {{ throw ""Printing defaults SDS not found: $defaultsPath"" }}

        Write-Output 'STEP>> ssdal import template'

        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $templatePath

        Write-Output 'STEP>> ssdal import cleanup'

        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $cleanupPath

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

        Write-Output 'STEP>> ssdal import printing defaults template'

        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $defaultsPath

        Write-Output 'STEP>> ssdal export after defaults import'

        Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action export -FilePath $defaultsExportPath

        $defaultsExportContent = Read-SdsFile -Path $defaultsExportPath

        try {{

            Test-GainschaPrintingDefaultsFromExport -Content $defaultsExportContent -ExpectedWidthMm $expectedWidthMm -ExpectedHeightMm $expectedHeightMm -ExpectedStockName $expectedStockName -ContextLabel 'Padrões de Impressão'

        }} catch {{

            $defaultsError = $_.Exception.Message

            if (Test-DeployUserHasInteractiveSession -DeployUser $deployUserName) {{

                Write-Output 'STEP>> interactive printing defaults fallback'

                Invoke-InteractivePrintingDefaultsSync -PrinterName $printerName

                Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action export -FilePath $defaultsExportPath

                $defaultsExportContent = Read-SdsFile -Path $defaultsExportPath

                try {{

                    Test-GainschaPrintingDefaultsFromExport -Content $defaultsExportContent -ExpectedWidthMm $expectedWidthMm -ExpectedHeightMm $expectedHeightMm -ExpectedStockName $expectedStockName -ContextLabel 'Padrões de Impressão'

                }} catch {{

                    throw ""Padrões de Impressão incorretos: esperado USER $expectedWidthMm x $expectedHeightMm mm. Detalhe: $($_.Exception.Message)""

                }}

            }} else {{

                throw ""Padrões de Impressão não aplicados — faça login no alvo como $deployUserName e reimplante. Detalhe: $defaultsError""

            }}

        }}

    }} finally {{

        if (Test-Path -LiteralPath $exportPath) {{ Remove-Item -LiteralPath $exportPath -Force -ErrorAction SilentlyContinue }}

        if (Test-Path -LiteralPath $defaultsExportPath) {{ Remove-Item -LiteralPath $defaultsExportPath -Force -ErrorAction SilentlyContinue }}

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


