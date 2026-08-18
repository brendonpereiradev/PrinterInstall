# Single-File Self-Contained publish for net8.0-windows WPF (win-x64).
# Packages .NET 8 Runtime, dependencies, embedded drivers, and configs into a single standalone executable.
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\PrinterInstall.App\PrinterInstall.App.csproj"
$publishDir = Join-Path $repoRoot "publish\PrinterInstall"
$coreDir = Join-Path $repoRoot "src\PrinterInstall.Core"
$driversZip = Join-Path $coreDir "Drivers\EmbeddedDrivers.zip"
$driversSource = Join-Path $repoRoot "drivers"

Push-Location $repoRoot
try {
    # 1. Garante que EmbeddedDrivers.zip existe e está atualizado
    if (Test-Path $driversSource) {
        Write-Host "[1/4] Compactando drivers para inclusão embutida no executável..." -ForegroundColor Cyan
        $driversDir = Join-Path $coreDir "Drivers"
        if (-not (Test-Path $driversDir)) {
            New-Item -ItemType Directory -Path $driversDir -Force | Out-Null
        }
        
        # Verifica se o zip precisa ser gerado ou atualizado
        $shouldZip = $true
        if (Test-Path $driversZip) {
            $zipTime = (Get-Item $driversZip).LastWriteTime
            $latestDriverFile = Get-ChildItem -Path $driversSource -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($latestDriverFile -and $latestDriverFile.LastWriteTime -le $zipTime) {
                $shouldZip = $false
                Write-Host "  -> EmbeddedDrivers.zip já está atualizado." -ForegroundColor DarkGray
            }
        }

        if ($shouldZip) {
            Write-Host "  -> Gerando EmbeddedDrivers.zip..." -ForegroundColor Yellow
            if (Test-Path $driversZip) { Remove-Item $driversZip -Force }
            Compress-Archive -Path "$driversSource\*" -DestinationPath $driversZip -Force
            $zipSizeMb = [math]::Round((Get-Item $driversZip).Length / 1MB, 2)
            Write-Host "  -> EmbeddedDrivers.zip gerado com sucesso ($zipSizeMb MB)." -ForegroundColor Green
        }
    }

    # 2. Limpa diretório de publicação anterior
    if (Test-Path $publishDir) {
        Write-Host "[2/4] Limpando diretório de publicação anterior..." -ForegroundColor Cyan
        Remove-Item "$publishDir\*" -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 3. Executa dotnet publish
    Write-Host "[3/4] Compilando e publicando executável único (Self-Contained win-x64)..." -ForegroundColor Cyan
    dotnet publish $project -c $Configuration /p:PublishProfile=WinDesktopFolder
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # 4. Limpeza de resíduos de compilação
    Write-Host "[4/4] Finalizando e validando integridade do pacote..." -ForegroundColor Cyan
    Get-ChildItem -Path $publishDir -Include *.pdb, *.deps.json, *.runtimeconfig.json -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

    $runtimesDir = Join-Path $publishDir "runtimes"
    if (Test-Path $runtimesDir) {
        Remove-Item $runtimesDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Validação do executável
    $exePath = Join-Path $publishDir "PrinterInstall.App.exe"
    if (-not (Test-Path $exePath)) {
        Write-Error "Erro: PrinterInstall.App.exe não foi encontrado na pasta de publicação."
    }

    $files = Get-ChildItem -Path $publishDir
    $exeSizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)

    Write-Host ""
    Write-Host "=================================================================" -ForegroundColor Green
    Write-Host "  PUBLICAÇÃO CONCLUÍDA COM SUCESSO! (ARQUIVO ÚNICO)" -ForegroundColor Green
    Write-Host "=================================================================" -ForegroundColor Green
    Write-Host "Diretório de saída: $publishDir"
    Write-Host "Executável gerado:  PrinterInstall.App.exe ($exeSizeMb MB)" -ForegroundColor Yellow
    Write-Host "Total de arquivos na pasta: $($files.Count) arquivo(s)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Distribuição: Apenas copie o arquivo 'PrinterInstall.App.exe' para as máquinas de destino."
    Write-Host ""
}
finally {
    Pop-Location
}
