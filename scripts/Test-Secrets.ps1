param([string]$ArtifactPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolDir = Join-Path ([System.IO.Path]::GetTempPath()) 'PrinterInstall-Gitleaks-8.30.1'
$tool = Join-Path $toolDir 'gitleaks.exe'
New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
# Pin the official portable release and verify it before running it.
$archive = Join-Path $toolDir 'gitleaks.zip'
Invoke-WebRequest -Uri 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $archive
$expectedHash = 'D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Gitleaks download checksum mismatch.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $toolDir -Force
& $tool git $repoRoot --log-opts='--all' --redact --no-banner
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($ArtifactPath) {
    & $tool dir $ArtifactPath --redact --no-banner --max-target-megabytes 500
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
