# IIoT Cloud Platform Deploy Script
# Usage: powershell -ExecutionPolicy Bypass -File .\deploy.ps1 [-SkipBuild] [-Tag latest]

param(
    [switch]$SkipBuild,
    [string]$Tag = "latest",
    [string]$Registry,
    [string]$DeployHost,
    [string]$DeployUser,
    [string]$DeployPort,
    [string]$StackName,
    [string]$DeployDir,
    [string]$PublicBaseUrl,
    [string]$AppHostDir = ".."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "=======================================" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "=======================================" -ForegroundColor Cyan
}

function Load-DotEnv([string]$path) {
    $values = @{}
    if (-not (Test-Path $path)) {
        return $values
    }

    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed.Split("=", 2)
        if ($parts.Count -ne 2) {
            continue
        }

        $values[$parts[0].Trim()] = $parts[1].Trim()
    }

    return $values
}

function Resolve-Setting([string]$name, [string]$parameterValue, [hashtable]$envValues) {
    if (-not [string]::IsNullOrWhiteSpace($parameterValue)) {
        return $parameterValue
    }

    if ($envValues.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace([string]$envValues[$name])) {
        return [string]$envValues[$name]
    }

    $processValue = [Environment]::GetEnvironmentVariable($name)
    if (-not [string]::IsNullOrWhiteSpace($processValue)) {
        return $processValue
    }

    return $null
}

function Assert-Configured([string]$name, [string]$value, [string[]]$invalidValues = @()) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required setting '$name' is missing. Provide it via script parameter, .env, or environment variable."
    }

    if ($invalidValues -contains $value) {
        throw "Setting '$name' is still using placeholder '$value'. Update .env or pass a real value."
    }
}

$scriptDir = Split-Path -Parent $PSCommandPath
$envPath = Join-Path $scriptDir ".env"
$envValues = Load-DotEnv $envPath

$resolvedRegistry = Resolve-Setting "IIOT_REGISTRY" $Registry $envValues
$resolvedDeployHost = Resolve-Setting "DEPLOY_HOST" $DeployHost $envValues
$resolvedDeployUser = Resolve-Setting "DEPLOY_USER" $DeployUser $envValues
$resolvedDeployPort = Resolve-Setting "DEPLOY_PORT" $DeployPort $envValues
$resolvedStackName = Resolve-Setting "STACK_NAME" $StackName $envValues
$resolvedDeployDir = Resolve-Setting "DEPLOY_DIR" $DeployDir $envValues
$resolvedPublicBaseUrl = Resolve-Setting "PUBLIC_BASE_URL" $PublicBaseUrl $envValues

Assert-Configured "IIOT_REGISTRY" $resolvedRegistry @("registry.example.com", "change-me-registry")
Assert-Configured "DEPLOY_HOST" $resolvedDeployHost @("deploy.example.com", "change-me-deploy-host")
Assert-Configured "DEPLOY_USER" $resolvedDeployUser @("deploy-user", "change-me-deploy-user")
Assert-Configured "DEPLOY_PORT" $resolvedDeployPort
Assert-Configured "STACK_NAME" $resolvedStackName
Assert-Configured "DEPLOY_DIR" $resolvedDeployDir
Assert-Configured "PUBLIC_BASE_URL" $resolvedPublicBaseUrl @("https://iiot.example.com", "http://iiot.example.com", "http://10.0.0.15")

if ($resolvedPublicBaseUrl.EndsWith("/")) {
    throw "PUBLIC_BASE_URL must not end with '/'. Example: http://10.0.0.15"
}

Write-Host ""
Write-Host "IIoT Cloud Platform Deploy" -ForegroundColor Yellow
Write-Host "Registry        : $resolvedRegistry" -ForegroundColor Gray
Write-Host "Deploy host     : $resolvedDeployHost" -ForegroundColor Gray
Write-Host "Deploy user     : $resolvedDeployUser" -ForegroundColor Gray
Write-Host "Public base URL : $resolvedPublicBaseUrl" -ForegroundColor Gray
Write-Host "Tag             : $Tag" -ForegroundColor Gray

Write-Step "Step 1/4  Login Registry"
& docker login $resolvedRegistry
if ($LASTEXITCODE -ne 0) {
    throw "FAILED: docker login $resolvedRegistry"
}
Write-Host "OK: registry login success" -ForegroundColor Green

if (-not $SkipBuild) {
    Write-Step "Step 2/4  Build and Push Docker Images"

    $aspirateExists = Get-Command aspirate -ErrorAction SilentlyContinue
    if (-not $aspirateExists) {
        Write-Host "Installing aspirate..." -ForegroundColor Yellow
        & dotnet tool install -g aspirate
        if ($LASTEXITCODE -ne 0) {
            throw "FAILED: dotnet tool install -g aspirate"
        }
    }

    $resolvedAppHostDir = Resolve-Path (Join-Path $scriptDir $AppHostDir)
    Push-Location $resolvedAppHostDir
    try {
        & aspirate build --container-registry $resolvedRegistry --container-image-tag $Tag --non-interactive
        if ($LASTEXITCODE -ne 0) {
            throw "FAILED: aspirate build"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "OK: build and push complete" -ForegroundColor Green
}
else {
    Write-Host "SKIP: Build" -ForegroundColor Yellow
}

Write-Step "Step 3/4  Sync Config Files"

if (-not (Test-Path $envPath)) {
    throw "Missing .env in $scriptDir. Copy .env.example to .env and fill the required deployment values first."
}

& ssh -p $resolvedDeployPort "${resolvedDeployUser}@${resolvedDeployHost}" "mkdir -p $resolvedDeployDir"
if ($LASTEXITCODE -ne 0) {
    throw "FAILED: create remote deploy directory"
}

foreach ($file in @(".env", "docker-compose.yaml", "nginx.conf")) {
    $fullPath = Join-Path $scriptDir $file
    if (-not (Test-Path $fullPath)) {
        throw "Required file '$file' was not found in $scriptDir."
    }

    Write-Host "Syncing $file ..." -ForegroundColor Gray
    & scp -P $resolvedDeployPort $fullPath "${resolvedDeployUser}@${resolvedDeployHost}:${resolvedDeployDir}/"
    if ($LASTEXITCODE -ne 0) {
        throw "FAILED: scp $file"
    }
}
Write-Host "OK: config files synced" -ForegroundColor Green

Write-Step "Step 4/4  Docker Stack Deploy"

$remoteCmd = "cd $resolvedDeployDir && export `$(grep -v '^#' .env | xargs) && docker stack deploy -c docker-compose.yaml $resolvedStackName --with-registry-auth"
& ssh -p $resolvedDeployPort "${resolvedDeployUser}@${resolvedDeployHost}" $remoteCmd
if ($LASTEXITCODE -ne 0) {
    throw "FAILED: docker stack deploy"
}
Write-Host "OK: stack deployed" -ForegroundColor Green

$apiUrl = "$resolvedPublicBaseUrl/api"

Write-Host ""
Write-Host "Deploy Complete!" -ForegroundColor Green
Write-Host "Public web : $resolvedPublicBaseUrl" -ForegroundColor Gray
Write-Host "Public API : $apiUrl" -ForegroundColor Gray
Write-Host "Dashboard  : http://${resolvedDeployHost}:18888 (manager node)" -ForegroundColor Gray
Write-Host ""

$check = Read-Host "Check service status? (y/n)"
if ($check -eq "y") {
    & ssh -p $resolvedDeployPort "${resolvedDeployUser}@${resolvedDeployHost}" "docker stack ps $resolvedStackName"
}
