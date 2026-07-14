[CmdletBinding()]
param(
    [switch]$SkipSolutionObservation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DotnetStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$LogDirectory,
        [switch]$AllowFailure
    )

    $safeName = ($Name -replace "[^A-Za-z0-9_-]", "_")
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $logPath = Join-Path $LogDirectory ("{0}_{1}.log" -f $safeName, $timestamp)

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray

    & dotnet @Arguments *>&1 | Tee-Object -FilePath $logPath | Out-Host
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        if ($AllowFailure) {
            Write-Warning ("Step failed (non-blocking): {0}" -f $Name)
            Write-Warning ("Log file: {0}" -f $logPath)
            return [pscustomobject]@{
                Name     = $Name
                Success  = $false
                ExitCode = $exitCode
                LogPath  = $logPath
            }
        }

        throw ("Step failed: {0}`nExit code: {1}`nLog file: {2}" -f $Name, $exitCode, $logPath)
    }

    return [pscustomobject]@{
        Name     = $Name
        Success  = $true
        ExitCode = 0
        LogPath  = $logPath
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$logDirectory = Join-Path $repoRoot "artifacts/build-baseline"
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Push-Location $repoRoot
try {
    Write-Host "Repository root: $repoRoot" -ForegroundColor Green
    $sdkVersion = (& dotnet --version).Trim()
    Write-Host "dotnet SDK: $sdkVersion" -ForegroundColor Green

    $steps = @(
        @{
            Name = "Build HttpApi"
            Args = @(
                "build",
                ".\src\hosts\IIoT.HttpApi\IIoT.HttpApi.csproj",
                "--no-restore",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            )
        },
        @{
            Name = "Build DataWorker"
            Args = @(
                "build",
                ".\src\hosts\IIoT.DataWorker\IIoT.DataWorker.csproj",
                "--no-restore",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            )
        },
        @{
            Name = "Build MigrationWorkApp"
            Args = @(
                "build",
                ".\src\hosts\IIoT.MigrationWorkApp\IIoT.MigrationWorkApp.csproj",
                "--no-restore",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            )
        },
        @{
            Name = "Build AppHost"
            Args = @(
                "build",
                ".\src\hosts\IIoT.AppHost\IIoT.AppHost.csproj",
                "--no-restore",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            )
        },
        @{
            Name = "Test Application layer"
            Args = @(
                "test",
                ".\src\tests\IIoT.CloudPlatform.ApplicationTests\IIoT.CloudPlatform.ApplicationTests.csproj",
                "--no-build",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            )
        }
    )

    $results = @()
    foreach ($step in $steps) {
        $result = Invoke-DotnetStep -Name $step.Name -Arguments $step.Args -LogDirectory $logDirectory
        $results += $result
    }

    if (-not $SkipSolutionObservation) {
        $solutionResult = Invoke-DotnetStep `
            -Name "Observe slnx build behavior" `
            -Arguments @(
                "build",
                ".\IIoT.CloudPlatform.slnx",
                "--no-restore",
                "-p:BuildInParallel=false",
                "-nologo",
                "-v:minimal"
            ) `
            -LogDirectory $logDirectory `
            -AllowFailure

        if (-not $solutionResult.Success) {
            Write-Warning "Known compatibility issue reproduced at solution entry."
            Write-Warning "See docs/slnx-build-compatibility.md for details and triage rules."
        } else {
            Write-Host "Solution observation passed in this run." -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "Baseline verification completed." -ForegroundColor Green
    Write-Host "Log directory: $logDirectory" -ForegroundColor Green
}
finally {
    Pop-Location
}
