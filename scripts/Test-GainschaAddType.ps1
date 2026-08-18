$ErrorActionPreference = 'Stop'

$root = 'C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2'

$validatePath = Join-Path $root 'src\PrinterInstall.Core\Gainscha\GainschaPrintingDefaultsSyncScriptFragment.cs'

$interactivePath = Join-Path $root 'src\PrinterInstall.Core\Gainscha\GainschaPrintingDefaultsInteractiveSyncScriptFragment.cs'



function Get-CSharpRawString {

    param([string]$Path)

    $src = Get-Content -LiteralPath $Path -Raw

    $start = $src.IndexOf('return """')
    if ($start -ge 0) {
        $start += 10
    } else {
        $start = $src.IndexOf('=>')
        if ($start -lt 0) { throw "Could not find raw string in $Path." }
        $start = $src.IndexOf('"""', $start)
        if ($start -lt 0) { throw "Could not find raw string in $Path." }
        $start += 3
    }

    $end = $src.IndexOf('""";', $start)

    if ($start -lt 10 -or $end -lt 0) { throw "Could not extract PowerShell fragment from $Path." }

    return $src.Substring($start, $end - $start)

}



$validateScript = Get-CSharpRawString -Path $validatePath

$interactiveScript = Get-CSharpRawString -Path $interactivePath



if ($validateScript -match 'Add-Type|DocumentProperties|Set-ItemProperty|SetPrinterData|printui\.dll') {

    throw 'Validate fragment must not use blocking Win32/registry APIs (headless path).'

}



if ($interactiveScript -notmatch 'Add-Type') {

    throw 'Interactive fallback fragment must include Add-Type for DocumentProperties sync.'

}

if ($interactiveScript -notmatch 'WaitForExit\(30000\)') {

    throw 'Interactive fallback must enforce 30 s subprocess timeout.'

}



Write-Host 'Gainscha printing-defaults fragments OK (validate-only headless + interactive fallback with timeout).'

