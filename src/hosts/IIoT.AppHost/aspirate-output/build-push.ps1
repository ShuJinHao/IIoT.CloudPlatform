# IIoT Cloud Platform Build and Push Script
# Place this file in: src/hosts/IIoT.AppHost/aspirate-output/
# Usage: powershell -ExecutionPolicy Bypass -File .\build-push.ps1 [-SkipBuild]

param(
    [switch]$SkipBuild,
    [string]$Tag = "latest"
)

$REGISTRY    = "10.98.90.154:80"
$APPHOST_DIR = ".."

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "=======================================" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "=======================================" -ForegroundColor Cyan
}

function Invoke-Step([string]$cmd) {
    Invoke-Expression $cmd
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $cmd" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "IIoT Build and Push" -ForegroundColor Yellow
Write-Host "Registry : $REGISTRY" -ForegroundColor Gray
Write-Host "Tag      : $Tag" -ForegroundColor Gray

# Step 1: Login Harbor
Write-Step "Step 1/2  Login Harbor"
Invoke-Step "docker login $REGISTRY"
Write-Host "OK: Harbor login success" -ForegroundColor Green

# Step 2: Build and Push
if (-not $SkipBuild) {
    Write-Step "Step 2/2  Build and Push"

    $aspirateExists = Get-Command aspirate -ErrorAction SilentlyContinue
    if (-not $aspirateExists) {
        Write-Host "Installing aspirate..." -ForegroundColor Yellow
        Invoke-Step "dotnet tool install -g aspirate"
    }

    Push-Location $APPHOST_DIR
    Invoke-Step "aspirate build --container-registry $REGISTRY --container-image-tag $Tag --non-interactive"
    Pop-Location

    Write-Host "OK: Build and Push complete" -ForegroundColor Green
} else {
    Write-Host "SKIP: Build" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Build and Push Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps (run in MobaXterm):" -ForegroundColor Yellow
Write-Host "  1. Upload .env / docker-compose.yaml / nginx.conf to /opt/iiot/" -ForegroundColor Gray
Write-Host "  2. cd /opt/iiot" -ForegroundColor Gray
Write-Host "  3. export `$(grep -v '^#' .env | xargs)" -ForegroundColor Gray
Write-Host "  4. docker stack deploy -c docker-compose.yaml iiot-app --with-registry-auth" -ForegroundColor Gray
Write-Host ""
