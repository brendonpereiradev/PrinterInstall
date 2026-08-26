param(

    [Parameter(Mandatory = $true)]

    [string] $PrinterName,



    [Parameter(Mandatory = $true)]

    [ValidateSet("Paciente", "Matrix", "Pulseira", "Lote")]

    [string] $Preset,



    [Parameter(Mandatory = $true)]

    [string] $OutputDirectory,



    [Parameter(Mandatory = $false)]

    [ValidateSet("Preferences", "PrintingDefaults")]

    [string] $Target = "Preferences"

)



$ErrorActionPreference = "Stop"



$expectedByPreset = @{

    Paciente = "USER (89,0 mm x 36,0 mm)"

    Matrix   = "USER (50,0 mm x 30,0 mm)"

    Pulseira = "USER (25,0 mm x 270,0 mm)"

    Lote     = "USER (45,0 mm x 13,0 mm)"

}



function Find-Ssdal {

    $roots = @(

        (Join-Path -Path $env:ProgramFiles -ChildPath "Seagull")

        (Join-Path -Path $env:ProgramFiles -ChildPath "Seagull Scientific")

        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Seagull")

        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Seagull Scientific")

    )



    foreach ($root in $roots) {

        if (-not (Test-Path $root)) { continue }

        $hit = Get-ChildItem -Path $root -Filter ssdal.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($hit) { return $hit.FullName }

    }



    throw "Seagull ssdal.exe not found. Install the Gainscha driver first."

}



$null = Get-Printer -Name $PrinterName -ErrorAction Stop

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null



$ssdal = Find-Ssdal

$baseName = $Preset.ToLowerInvariant()

$fileName = if ($Target -eq "PrintingDefaults") { "$baseName-defaults.sds" } else { "$baseName.sds" }

$outputPath = Join-Path $OutputDirectory $fileName



if ($Target -eq "Preferences") {

    Write-Host "Configure ONLY Printing preferences (Properties -> Preferences -> Page Setup) before export."

} else {

    Write-Host "Configure ONLY Printing Defaults (Properties -> Advanced -> Printing Defaults -> Page Setup) before export."

    Write-Host "Do NOT change Printing preferences — they should remain at driver default for a clean defaults capture."

}



& $ssdal /p $PrinterName /q settings export $outputPath

if ($LASTEXITCODE -ne 0) {

    throw "ssdal settings export failed with exit code $LASTEXITCODE"

}



$content = Get-Content -LiteralPath $outputPath -Raw

$stockMatches = [regex]::Matches($content, '<stock>\s*Name=([^\r\n]+)', 'IgnoreCase')

$stocks = @($stockMatches | ForEach-Object { $_.Groups[1].Value.Trim() })

$expected = $expectedByPreset[$Preset]



if ($stocks -notcontains $expected) {

    throw "Export does not contain expected stock '$expected'. Configure the queue manually first. Found: $($stocks -join ', ')"

}



Write-Host "Exported $Preset $Target template to $outputPath"

Write-Host "Next: copy this file to src/PrinterInstall.Core/Gainscha/Templates/$fileName"


