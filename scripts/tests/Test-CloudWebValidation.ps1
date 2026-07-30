[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-Selection {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][bool]$Affected,
        [Parameter(Mandatory)][bool]$Full,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ChangedFiles
    )

    [ordered]@{
        schemaVersion = 2
        mode = if ($Full) { 'Full' } else { 'Default' }
        headRef = 'HEAD'
        web = [ordered]@{
            affected = $Affected
            full = $Full
            changedFiles = $ChangedFiles
        }
    } |
        ConvertTo-Json -Depth 6 |
        Set-Content $Path -Encoding utf8NoBOM
}

function Invoke-Validator {
    param(
        [Parameter(Mandatory)][string]$Validator,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Selection,
        [Parameter(Mandatory)][string]$Evidence
    )

    & $Validator `
        -RepositoryRoot $Repository `
        -SelectionPath $Selection `
        -OutputPath $Evidence
}

$fixtureBase = Join-Path ([IO.Path]::GetTempPath()) (
    "iiot-cloud-web-validation-$([Guid]::NewGuid().ToString('N'))")
$fixtureRepository = Join-Path $fixtureBase 'repository'
$fakeBin = Join-Path $fixtureBase 'fake-bin'
$commandLog = Join-Path $fixtureBase 'commands.log'
$selectionPath = Join-Path $fixtureRepository 'artifacts/ci-selection.json'
$evidencePath = Join-Path $fixtureRepository 'artifacts/current-web-validation.json'
$validator = Join-Path $PSScriptRoot 'Invoke-CloudWebValidation.ps1'
$originalPath = $env:PATH

try {
    [void](New-Item (Join-Path $fixtureRepository 'src/ui/iiot-web/src') -ItemType Directory -Force)
    [void](New-Item (Split-Path $selectionPath -Parent) -ItemType Directory -Force)
    [void](New-Item $fakeBin -ItemType Directory -Force)

    '<Solution />' | Set-Content (Join-Path $fixtureRepository 'IIoT.CloudPlatform.slnx') -Encoding utf8NoBOM
    '{}' | Set-Content (Join-Path $fixtureRepository 'src/ui/iiot-web/package-lock.json') -Encoding utf8NoBOM
    'export const value = 1;' |
        Set-Content (Join-Path $fixtureRepository 'src/ui/iiot-web/src/example.ts') -Encoding utf8NoBOM
    "artifacts/`n" | Set-Content (Join-Path $fixtureRepository '.gitignore') -Encoding utf8NoBOM

    @'
#!/usr/bin/env bash
set -euo pipefail
printf 'npm %s\n' "$*" >> "$CLOUD_WEB_FAKE_LOG"
if [[ "${CLOUD_WEB_FAKE_FAIL:-}" == 'build' && "$*" == 'run build' ]]; then
  exit 9
fi
'@ | Set-Content (Join-Path $fakeBin 'npm') -Encoding utf8NoBOM
    @'
#!/usr/bin/env bash
set -euo pipefail
printf 'npx %s\n' "$*" >> "$CLOUD_WEB_FAKE_LOG"
if [[ "${CLOUD_WEB_FAKE_FAIL:-}" == 'vitest' ]]; then
  exit 7
fi
report=''
for argument in "$@"; do
  case "$argument" in
    --outputFile.json=*) report="${argument#--outputFile.json=}" ;;
  esac
done
if [[ -z "$report" ]]; then
  exit 8
fi
printf '%s\n' '{"success":true,"numPassedTests":3,"numFailedTests":0,"numPendingTests":0}' > "$report"
'@ | Set-Content (Join-Path $fakeBin 'npx') -Encoding utf8NoBOM
    & chmod +x (Join-Path $fakeBin 'npm') (Join-Path $fakeBin 'npx')
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to make the Cloud Web validation test shims executable.'
    }

    & git -C $fixtureRepository init --quiet
    & git -C $fixtureRepository config user.email 'cloud-web-validation@example.invalid'
    & git -C $fixtureRepository config user.name 'Cloud Web Validation Test'
    & git -C $fixtureRepository add .
    & git -C $fixtureRepository commit --quiet -m 'fixture'
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the Cloud Web validation fixture commit.'
    }
    $sourceSha = ((& git -C $fixtureRepository rev-parse HEAD) -join "`n").Trim()

    $env:PATH = "$fakeBin$([IO.Path]::PathSeparator)$originalPath"
    $env:CLOUD_WEB_FAKE_LOG = $commandLog
    Remove-Item Env:CLOUD_WEB_FAKE_FAIL -ErrorAction SilentlyContinue

    Write-Selection `
        -Path $selectionPath `
        -Affected $false `
        -Full $false `
        -ChangedFiles @()
    Invoke-Validator `
        -Validator $validator `
        -Repository $fixtureRepository `
        -Selection $selectionPath `
        -Evidence $evidencePath
    $noOpEvidence = Get-Content $evidencePath -Raw | ConvertFrom-Json
    Assert-True ($noOpEvidence.schemaVersion -eq 1) 'No-op Web evidence schema is invalid.'
    Assert-True ($noOpEvidence.sourceSha -ceq $sourceSha) 'No-op Web evidence source SHA is invalid.'
    Assert-True (-not [bool]$noOpEvidence.affected) 'No-op Web evidence was marked affected.'
    Assert-True ($noOpEvidence.scope.fileCount -eq 0) 'No-op Web scope must be empty.'
    Assert-True ($noOpEvidence.buildResult.status -ceq 'skipped') 'No-op Web build was not skipped.'
    Assert-True ($noOpEvidence.testResult.mode -ceq 'no-op') 'No-op Web test mode is invalid.'
    Assert-True (-not (Test-Path $commandLog -PathType Leaf)) 'No-op Web validation invoked npm/npx.'

    $relatedFile = 'src/ui/iiot-web/src/example.ts'
    Write-Selection `
        -Path $selectionPath `
        -Affected $true `
        -Full $false `
        -ChangedFiles @($relatedFile)
    Invoke-Validator `
        -Validator $validator `
        -Repository $fixtureRepository `
        -Selection $selectionPath `
        -Evidence $evidencePath
    $relatedEvidence = Get-Content $evidencePath -Raw | ConvertFrom-Json
    $expectedScopeDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes($relatedFile))).ToLowerInvariant()
    Assert-True ([bool]$relatedEvidence.affected) 'Related Web evidence was not marked affected.'
    Assert-True (-not [bool]$relatedEvidence.full) 'Related Web evidence was marked full.'
    Assert-True ($relatedEvidence.scope.fileCount -eq 1) 'Related Web scope count is invalid.'
    Assert-True ($relatedEvidence.scope.sha256 -ceq $expectedScopeDigest) 'Related Web scope digest is invalid.'
    Assert-True ($relatedEvidence.dependencyInstallResult.status -ceq 'passed') 'npm ci did not pass.'
    Assert-True ($relatedEvidence.buildResult.status -ceq 'passed') 'Production Web build did not pass.'
    Assert-True ($relatedEvidence.testResult.mode -ceq 'related') 'Related Web test mode is invalid.'
    Assert-True ($relatedEvidence.testResult.passed -eq 3) 'Related Web passed count is invalid.'
    $relatedCommands = Get-Content $commandLog -Raw
    Assert-True ($relatedCommands.Contains("npm ci`n", [StringComparison]::Ordinal)) 'Related validation omitted npm ci.'
    Assert-True ($relatedCommands.Contains("npm run build`n", [StringComparison]::Ordinal)) 'Related validation omitted production build.'
    Assert-True ($relatedCommands.Contains('vitest related src/example.ts', [StringComparison]::Ordinal)) 'Related validation omitted Vitest related scope.'

    Remove-Item $commandLog -Force
    Write-Selection `
        -Path $selectionPath `
        -Affected $true `
        -Full $true `
        -ChangedFiles @()
    Invoke-Validator `
        -Validator $validator `
        -Repository $fixtureRepository `
        -Selection $selectionPath `
        -Evidence $evidencePath
    $fullEvidence = Get-Content $evidencePath -Raw | ConvertFrom-Json
    Assert-True ([bool]$fullEvidence.full) 'Full Web evidence was not marked full.'
    Assert-True ($fullEvidence.testResult.mode -ceq 'full') 'Full Web test mode is invalid.'
    $fullCommands = Get-Content $commandLog -Raw
    Assert-True ($fullCommands.Contains('vitest run', [StringComparison]::Ordinal)) 'Full validation omitted the complete Vitest run.'

    $env:CLOUD_WEB_FAKE_FAIL = 'build'
    $failed = $false
    try {
        Invoke-Validator `
            -Validator $validator `
            -Repository $fixtureRepository `
            -Selection $selectionPath `
            -Evidence $evidencePath
    } catch {
        $failed = $true
    }
    Assert-True $failed 'Failed Web build unexpectedly succeeded.'
    Assert-True (-not (Test-Path $evidencePath -PathType Leaf)) 'Failed Web build left success evidence.'

    Write-Host 'CLOUD_WEB_VALIDATION_CONTRACT_OK scenarios=no-op,related,full,failure'
} finally {
    $env:PATH = $originalPath
    Remove-Item Env:CLOUD_WEB_FAKE_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:CLOUD_WEB_FAKE_FAIL -ErrorAction SilentlyContinue
    if (Test-Path $fixtureBase -PathType Container) {
        Remove-Item $fixtureBase -Recurse -Force
    }
}
