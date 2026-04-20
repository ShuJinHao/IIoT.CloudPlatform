# IIoT Cloud Platform Build and Push Script
# Usage: powershell -ExecutionPolicy Bypass -File .\build-push.ps1 [-SkipBuild] [-Tag latest]

param(
    [switch]$SkipBuild,
    [string]$Tag = "latest",
    [string]$Registry,
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
$envValues = Load-DotEnv (Join-Path $scriptDir ".env")
$resolvedRegistry = Resolve-Setting "IIOT_REGISTRY" $Registry $envValues

Assert-Configured "IIOT_REGISTRY" $resolvedRegistry @("registry.example.com", "change-me-registry")

Write-Host ""
Write-Host "IIoT Build and Push" -ForegroundColor Yellow
Write-Host "Registry : $resolvedRegistry" -ForegroundColor Gray
Write-Host "Tag      : $Tag" -ForegroundColor Gray

Write-Step "Step 1/2  Login Registry"
& docker login $resolvedRegistry
if ($LASTEXITCODE -ne 0) {
    throw "FAILED: docker login $resolvedRegistry"
}
Write-Host "OK: registry login success" -ForegroundColor Green

if (-not $SkipBuild) {
    Write-Step "Step 2/2  Build and Push"

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

Write-Host ""
Write-Host "Build and Push Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Copy .env.example to .env and fill IIOT_REGISTRY / DEPLOY_* / PUBLIC_BASE_URL." -ForegroundColor Gray
Write-Host "  2. Run deploy.ps1 or upload .env / docker-compose.yaml / nginx.conf to the deployment host." -ForegroundColor Gray
Write-Host ""
