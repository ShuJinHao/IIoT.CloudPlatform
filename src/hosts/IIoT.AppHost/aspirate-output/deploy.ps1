# IIoT Cloud Platform Deploy Script
# Place this file in: src/hosts/IIoT.AppHost/aspirate-output/
# Usage: powershell -ExecutionPolicy Bypass -File .\deploy.ps1 [-SkipBuild]

param(
    [switch]$SkipBuild,
    [string]$Tag = "latest"
)

# Config
$REGISTRY    = "10.98.90.154:80"
$SERVER_IP   = "10.98.90.154"
$SERVER_USER = "root"
$SERVER_PORT = "22"
$STACK_NAME  = "iiot-app"
$DEPLOY_DIR  = "/opt/iiot"
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
Write-Host "IIoT Cloud Platform Deploy" -ForegroundColor Yellow
Write-Host "Registry : $REGISTRY" -ForegroundColor Gray
Write-Host "Server   : $SERVER_IP" -ForegroundColor Gray
Write-Host "Tag      : $Tag" -ForegroundColor Gray

# Step 1: Login Harbor
Write-Step "Step 1/4  Login Harbor"
Invoke-Step "docker login $REGISTRY"
Write-Host "OK: Harbor login success" -ForegroundColor Green

# Step 2: Build and Push images via aspirate
if (-not $SkipBuild) {
    Write-Step "Step 2/4  Build and Push Docker Images"

    $aspirateExists = Get-Command aspirate -ErrorAction SilentlyContinue
    if (-not $aspirateExists) {
        Write-Host "Installing aspirate..." -ForegroundColor Yellow
        Invoke-Step "dotnet tool install -g aspirate"
    }

    Push-Location $APPHOST_DIR
    # aspirate build handles both build and push automatically
    Invoke-Step "aspirate build --container-registry $REGISTRY --container-image-tag $Tag --non-interactive"
    Pop-Location

    Write-Host "OK: Build and Push complete" -ForegroundColor Green
} else {
    Write-Host "SKIP: Build" -ForegroundColor Yellow
}

# Step 3: Sync config files to server
Write-Step "Step 3/4  Sync Config Files"

ssh -p $SERVER_PORT "${SERVER_USER}@${SERVER_IP}" "mkdir -p $DEPLOY_DIR"

foreach ($file in @(".env", "docker-compose.yaml", "nginx.conf")) {
    if (Test-Path $file) {
        Write-Host "Syncing $file ..." -ForegroundColor Gray
        scp -P $SERVER_PORT $file "${SERVER_USER}@${SERVER_IP}:${DEPLOY_DIR}/"
    } else {
        Write-Host "SKIP: $file not found" -ForegroundColor Yellow
    }
}
Write-Host "OK: Config files synced" -ForegroundColor Green

# Step 4: Deploy stack on server
Write-Step "Step 4/4  Docker Stack Deploy"

$remoteCmd = "cd $DEPLOY_DIR && export `$(grep -v '^#' .env | xargs) && docker stack deploy -c docker-compose.yaml $STACK_NAME --with-registry-auth"
ssh -p $SERVER_PORT "${SERVER_USER}@${SERVER_IP}" $remoteCmd

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: Stack deploy" -ForegroundColor Red
    exit 1
}
Write-Host "OK: Stack deployed" -ForegroundColor Green

# Done
Write-Host ""
Write-Host "Deploy Complete!" -ForegroundColor Green
Write-Host "Dashboard : http://${SERVER_IP}:18888" -ForegroundColor Gray
Write-Host "API       : http://${SERVER_IP}:81/api" -ForegroundColor Gray
Write-Host "Web       : http://${SERVER_IP}:81" -ForegroundColor Gray
Write-Host ""

$check = Read-Host "Check service status? (y/n)"
if ($check -eq "y") {
    ssh -p $SERVER_PORT "${SERVER_USER}@${SERVER_IP}" "docker stack ps $STACK_NAME"
}
