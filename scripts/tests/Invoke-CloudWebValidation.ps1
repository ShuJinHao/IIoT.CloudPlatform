[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$SelectionPath = 'artifacts/ci-selection.json',
    [string]$OutputPath = 'artifacts/ci-test-results/current-web-validation.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function ConvertTo-CanonicalWebPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path.Replace('\', '/').Trim()
    while ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        [IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith('../', [StringComparison]::Ordinal) -or
        -not $normalized.StartsWith('src/ui/iiot-web/', [StringComparison]::Ordinal) -or
        $normalized -match '(?:^|/)\.\.(?:/|$)') {
        throw "Cloud Web selection contains an invalid repository path: '$Path'."
    }
    return $normalized
}

function Get-GitCommit {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Reference
    )

    if ($Reference -cne 'HEAD' -and $Reference -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Cloud Web selection headRef must be HEAD or a full commit SHA: '$Reference'."
    }
    $resolved = ((& git -C $Root rev-parse --verify "$Reference^{commit}" 2>&1) -join "`n").Trim()
    if ($LASTEXITCODE -ne 0 -or $resolved -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Cloud Web selection headRef cannot be resolved to a full commit SHA: '$Reference'."
    }
    return $resolved.ToLowerInvariant()
}

function Get-Sha256 {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Values)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]::Join("`n", $Values))
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$root = (Resolve-Path $RepositoryRoot).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$webRoot = Join-Path $root 'src/ui/iiot-web'
if (-not (Test-Path (Join-Path $webRoot 'package-lock.json') -PathType Leaf) -or
    -not (Test-Path (Join-Path $root 'IIoT.CloudPlatform.slnx') -PathType Leaf)) {
    throw "Cloud repository root is invalid: $root"
}

$resolvedSelection = Resolve-RepositoryFile -Root $root -Path $SelectionPath
$resolvedOutput = Resolve-RepositoryFile -Root $root -Path $OutputPath
if (Test-Path $resolvedOutput -PathType Leaf) {
    Remove-Item $resolvedOutput -Force
}
if (-not (Test-Path $resolvedSelection -PathType Leaf)) {
    throw "Cloud CI selection is missing: $resolvedSelection"
}

$selection = Get-Content $resolvedSelection -Raw | ConvertFrom-Json
if ($selection.schemaVersion -isnot [long] -and $selection.schemaVersion -isnot [int]) {
    throw 'Cloud CI selection schemaVersion must be an integer.'
}
if ([int]$selection.schemaVersion -ne 2) {
    throw "Unsupported Cloud CI selection schema: $($selection.schemaVersion)"
}
if ($null -eq $selection.web) {
    throw 'Cloud CI selection schema v2 is missing the web scope.'
}
if ($selection.web.affected -isnot [bool] -or $selection.web.full -isnot [bool]) {
    throw 'Cloud CI selection web.affected and web.full must be booleans.'
}

$affected = [bool]$selection.web.affected
$full = [bool]$selection.web.full
$rawChangedFiles = @($selection.web.changedFiles)
$changedFiles = [Collections.Generic.List[string]]::new()
foreach ($file in $rawChangedFiles) {
    if ($file -isnot [string]) {
        throw 'Cloud CI selection web.changedFiles must contain only strings.'
    }
    $changedFiles.Add((ConvertTo-CanonicalWebPath -Path $file))
}
[string[]]$canonicalChangedFiles = @($changedFiles | Sort-Object -Unique)
if ($canonicalChangedFiles.Count -ne $rawChangedFiles.Count) {
    throw 'Cloud CI selection web.changedFiles must be sorted and unique.'
}
for ($index = 0; $index -lt $canonicalChangedFiles.Count; $index++) {
    if ($canonicalChangedFiles[$index] -cne [string]$rawChangedFiles[$index]) {
        throw 'Cloud CI selection web.changedFiles must use canonical sorted repository paths.'
    }
}
if ($full -and -not $affected) {
    throw 'Cloud CI selection cannot request full Web validation while web.affected is false.'
}
if (-not $affected -and $canonicalChangedFiles.Count -ne 0) {
    throw 'Cloud CI selection cannot contain Web changed files while web.affected is false.'
}
if ($affected -and -not $full -and $canonicalChangedFiles.Count -eq 0) {
    throw 'Affected non-full Cloud Web selection must contain at least one changed file.'
}

$headRef = [string]$selection.headRef
$sourceSha = Get-GitCommit -Root $root -Reference $headRef
$currentHead = Get-GitCommit -Root $root -Reference 'HEAD'
if ($sourceSha -cne $currentHead) {
    throw "Cloud Web selection source does not match the checked-out HEAD: selection=$sourceSha head=$currentHead"
}
$worktreeState = @(& git -C $root status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the Cloud worktree before Web validation.'
}
if ($worktreeState.Count -ne 0) {
    throw "Cloud Web validation requires a clean exact-SHA worktree:`n$($worktreeState -join "`n")"
}

$scopeSha256 = Get-Sha256 -Values $canonicalChangedFiles
$dependencyInstallResult = [ordered]@{
    status = if ($affected) { 'pending' } else { 'skipped' }
    exitCode = if ($affected) { $null } else { 0 }
}
$buildResult = [ordered]@{
    status = if ($affected) { 'pending' } else { 'skipped' }
    exitCode = if ($affected) { $null } else { 0 }
}
$testResult = [ordered]@{
    mode = if (-not $affected) { 'no-op' } elseif ($full) { 'full' } else { 'related' }
    passed = 0
    failed = 0
    skipped = 0
}

$vitestOutput = Join-Path ([IO.Path]::GetTempPath()) (
    "iiot-cloud-web-vitest-$([Guid]::NewGuid().ToString('N')).json")
try {
    if ($affected) {
        Push-Location $webRoot
        try {
            & npm ci
            $dependencyInstallResult.exitCode = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) {
                throw "Cloud Web dependency installation failed with exit code $LASTEXITCODE."
            }
            $dependencyInstallResult.status = 'passed'

            & npm run build
            $buildResult.exitCode = $LASTEXITCODE
            if ($LASTEXITCODE -ne 0) {
                throw "Cloud Web production build failed with exit code $LASTEXITCODE."
            }
            $buildResult.status = 'passed'

            $vitestArguments = @(
                '--no-install',
                'vitest'
            )
            if ($full) {
                $vitestArguments += @('run')
            } else {
                $relativeFiles = @($canonicalChangedFiles | ForEach-Object {
                        $_.Substring('src/ui/iiot-web/'.Length)
                    })
                $vitestArguments += @('related') + $relativeFiles + @('--run', '--passWithNoTests')
            }
            $vitestArguments += @(
                '--reporter=default',
                '--reporter=json',
                "--outputFile.json=$vitestOutput"
            )
            & npx @vitestArguments
            $vitestExitCode = $LASTEXITCODE
            if ($vitestExitCode -ne 0) {
                throw "Cloud Web $($testResult.mode) Vitest failed with exit code $vitestExitCode."
            }
        } finally {
            Pop-Location
        }

        if (-not (Test-Path $vitestOutput -PathType Leaf)) {
            throw 'Cloud Web Vitest did not produce its JSON result.'
        }
        $vitest = Get-Content $vitestOutput -Raw | ConvertFrom-Json
        $testResult.passed = [int]$vitest.numPassedTests
        $testResult.failed = [int]$vitest.numFailedTests
        $testResult.skipped = [int]$vitest.numPendingTests
        if (-not [bool]$vitest.success -or
            $testResult.failed -ne 0 -or
            $testResult.skipped -ne 0) {
            throw "Cloud Web Vitest result did not reconcile: passed=$($testResult.passed) failed=$($testResult.failed) skipped=$($testResult.skipped)"
        }
    }

    $postValidationWorktreeState = @(
        & git -C $root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Cloud worktree after Web validation.'
    }
    if ($postValidationWorktreeState.Count -ne 0) {
        throw "Cloud Web validation changed the exact-SHA worktree:`n$($postValidationWorktreeState -join "`n")"
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        sourceSha = $sourceSha
        scope = [ordered]@{
            fileCount = $canonicalChangedFiles.Count
            sha256 = $scopeSha256
        }
        affected = $affected
        full = $full
        changedFiles = $canonicalChangedFiles
        dependencyInstallResult = $dependencyInstallResult
        buildResult = $buildResult
        testResult = $testResult
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }

    $outputDirectory = Split-Path $resolvedOutput -Parent
    [void](New-Item $outputDirectory -ItemType Directory -Force)
    $temporaryEvidence = Join-Path $outputDirectory (
        ".$([IO.Path]::GetFileName($resolvedOutput)).$([Guid]::NewGuid().ToString('N')).tmp")
    try {
        $evidence |
            ConvertTo-Json -Depth 8 |
            Set-Content $temporaryEvidence -Encoding utf8NoBOM
        Move-Item $temporaryEvidence $resolvedOutput -Force
    } finally {
        if (Test-Path $temporaryEvidence -PathType Leaf) {
            Remove-Item $temporaryEvidence -Force
        }
    }
} finally {
    if (Test-Path $vitestOutput -PathType Leaf) {
        Remove-Item $vitestOutput -Force
    }
}

Write-Host "CLOUD_WEB_VALIDATION_OK affected=$affected full=$full scope=$($canonicalChangedFiles.Count) source=$sourceSha output=$resolvedOutput"
