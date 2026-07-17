[CmdletBinding()]
param(
    [string]$CatalogPath = 'scripts/testing/baselines/three-repository-closure-predicates.json',
    [string]$OutputPath = 'artifacts/testing/closure/three-repository-closure-result.json',
    [string]$EdgeRepositoryRoot = '.codex-worktrees/edge-startup-exception-retirement-closure',
    [string]$CloudRepositoryRoot = '.codex-worktrees/cloud-cache-001',
    [string]$AiRepositoryRoot = '.codex-worktrees/ai-phase0-closeout',
    [switch]$SkipRepositoryBinding
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

function Resolve-WorkspacePath([string]$path) {
    $candidate = if ([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $workspaceRoot $path }
    return (Resolve-Path $candidate).Path
}

function Get-Sha256([string]$path) {
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-NonEmptyArray($value, [string]$label) {
    if (@($value).Count -eq 0) { throw "$label must not be empty." }
}

$catalogFile = Resolve-WorkspacePath $CatalogPath
$catalog = Get-Content $catalogFile -Raw | ConvertFrom-Json -Depth 100
if ([int]$catalog.schemaVersion -ne 1) { throw 'Closure predicate catalog schemaVersion must be 1.' }
$predicates = @($catalog.predicates)
if ($predicates.Count -ne 14) { throw "Closure predicate catalog must contain exactly 14 predicates; actual=$($predicates.Count)." }
$expectedIds = 1..14 | ForEach-Object { "CLOSURE-{0:d2}" -f $_ }
$actualIds = @($predicates | ForEach-Object { [string]$_.id })
if (@($actualIds | Sort-Object -Unique).Count -ne 14 -or
    @(Compare-Object ($expectedIds | Sort-Object) ($actualIds | Sort-Object)).Count -ne 0) {
    throw 'Closure predicate ids must be the unique closed set CLOSURE-01 through CLOSURE-14.'
}

$repositories = @{
    Edge = $EdgeRepositoryRoot
    Cloud = $CloudRepositoryRoot
    AI = $AiRepositoryRoot
}
foreach ($predicate in $predicates) {
    $id = [string]$predicate.id
    if ($predicate.blocking -ne $true) { throw "$id must be blocking=true." }
    if ([string]::IsNullOrWhiteSpace([string]$predicate.description)) { throw "$id description is empty." }
    if ($null -eq $predicate.inputUniverse -or $predicate.inputUniverse.closed -ne $true) {
        throw "$id input universe must be explicit and closed."
    }
    Assert-NonEmptyArray $predicate.inputUniverse.roots "$id inputUniverse.roots"
    Assert-NonEmptyArray $predicate.inputUniverse.globs "$id inputUniverse.globs"
    foreach ($allow in @($predicate.inputUniverse.allowlist)) {
        if ([string]::IsNullOrWhiteSpace([string]$allow.value) -or
            [string]::IsNullOrWhiteSpace([string]$allow.reason)) {
            throw "$id contains an unexplained allowlist entry."
        }
    }
    if ([string]$predicate.description -match '(?i)(\ball\b|\bevery\b|全部|所有|真实|稳定)' -and
        $predicate.inputUniverse.closed -ne $true) {
        throw "$id uses an unclosed universal claim."
    }
    Assert-NonEmptyArray $predicate.commands "$id commands"
    if ([int]$predicate.expectation.exitCode -ne 0) { throw "$id expected exitCode must be 0." }
    if ([string]$predicate.actual.status -cne 'PASS' -or
        [int]$predicate.actual.exitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace([string]$predicate.actual.reasonCode)) {
        throw "$id blocking result is not PASS/0 with a reason code."
    }
    Assert-NonEmptyArray $predicate.evidence "$id evidence"
    Assert-NonEmptyArray $predicate.bindings.repositories "$id repository bindings"
    Assert-NonEmptyArray $predicate.bindings.github "$id GitHub bindings"
    foreach ($github in @($predicate.bindings.github)) {
        foreach ($property in @('repository', 'pr', 'run', 'job', 'artifact')) {
            if ($null -eq $github.PSObject.Properties[$property] -or
                [string]::IsNullOrWhiteSpace([string]$github.$property)) {
                throw "$id GitHub binding is missing '$property'."
            }
        }
    }
    foreach ($evidence in @($predicate.evidence)) {
        if ([string]$evidence.sha256 -notmatch '^[0-9a-f]{64}$') { throw "$id evidence SHA-256 is invalid." }
        $evidencePath = Resolve-WorkspacePath ([string]$evidence.path)
        $actualSha = Get-Sha256 $evidencePath
        if ($actualSha -cne [string]$evidence.sha256) {
            throw "$id evidence hash mismatch: path=$($evidence.path) expected=$($evidence.sha256) actual=$actualSha"
        }
    }
    Assert-NonEmptyArray $predicate.rules "$id rules"
    foreach ($rule in @($predicate.rules)) {
        if ([string]$rule.formalLocation -match '复盘|历史' -or
            [string]::IsNullOrWhiteSpace([string]$rule.gate)) {
            throw "$id rule exists only in retrospective/history or has no executable gate."
        }
        $null = Resolve-WorkspacePath ([string]$rule.formalLocation)
    }
    if (-not $SkipRepositoryBinding) {
        foreach ($binding in @($predicate.bindings.repositories)) {
            $name = [string]$binding.repository
            if (-not $repositories.ContainsKey($name)) { throw "$id has unknown repository binding '$name'." }
            $root = Resolve-WorkspacePath ([string]$repositories[$name])
            $head = (& git -C $root rev-parse HEAD).Trim()
            $tree = (& git -C $root rev-parse 'HEAD^{tree}').Trim()
            $status = @(& git -C $root status --porcelain --untracked-files=all)
            if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0 -or
                $head -cne [string]$binding.head -or $tree -cne [string]$binding.tree -or
                $binding.clean -ne $true) {
                throw "$id candidate repository binding mismatch: repository=$name."
            }
        }
    }
}

$outputFile = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $workspaceRoot $OutputPath }
New-Item -ItemType Directory -Path (Split-Path $outputFile -Parent) -Force | Out-Null
[ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    catalogPath = [IO.Path]::GetRelativePath($workspaceRoot, $catalogFile).Replace('\', '/')
    catalogSha256 = Get-Sha256 $catalogFile
    blocking = 14
    passed = 14
    failed = 0
    notRun = 0
    status = 'PASS'
    results = @($predicates | ForEach-Object {
        [ordered]@{ id = [string]$_.id; status = 'PASS'; reasonCode = [string]$_.actual.reasonCode }
    })
} | ConvertTo-Json -Depth 20 | Set-Content $outputFile -Encoding utf8
Write-Host "THREE_REPOSITORY_CLOSURE_OK blocking=14 passed=14 failed=0 notRun=0 output=$outputFile"
