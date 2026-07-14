#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [string]$BaselinePath = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$pinnedBaseCommit = '88c41109fbcf0b87b18939a139e0bff751e03d07'
$pinnedBaseSourcePath = 'src/tests'
$pinnedBaseTestsTreeObject = '604fc2c1f2b9e4746657f314191a4514ccf75530'
$pinnedBaseDeclarationDigestSha256 = 'd75d5529e418be31819676abeb210a79550149e14896fbe1857c30a83149c77c'
$pinnedBaseDeclarationCount = 529

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $repoRoot "scripts/tests/baselines/cloud-test-migration.json"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts/test-results/cloud-test-migration.json"
}

function Invoke-GitText {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = @(& git -C $repoRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Get-TestDeclarations {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $normalizedPath = $Path.Replace('\', '/')
    $lines = [regex]::Split($Content, "\r?\n")
    $declarations = [System.Collections.Generic.List[object]]::new()

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -notmatch '^\s*\[(Fact|Theory)(?:Attribute)?(?:\([^\r\n]*\))?\]\s*$') {
            continue
        }

        $kind = $Matches[1]
        $methodName = $null
        for ($candidate = $index + 1; $candidate -lt [Math]::Min($lines.Count, $index + 40); $candidate++) {
            $line = $lines[$candidate]
            if ([string]::IsNullOrWhiteSpace($line) -or $line -match '^\s*\[') {
                continue
            }

            $signatureEnd = [Math]::Min($lines.Count - 1, $candidate + 8)
            $signature = ($lines[$candidate..$signatureEnd] -join ' ')
            if ($signature -match '^\s*(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override|sealed|new|partial)\s+)*[A-Za-z0-9_<>,\.\?\[\]:]+\s+(?<method>[A-Za-z_]\w*)\s*\(') {
                $methodName = $Matches['method']
                break
            }

            throw "Could not parse test declaration after ${normalizedPath}:$($index + 1): $line"
        }

        if ([string]::IsNullOrWhiteSpace($methodName)) {
            throw "Could not find test method after ${normalizedPath}:$($index + 1)."
        }

        $declarations.Add([pscustomobject]@{
            id = "$normalizedPath::$methodName"
            path = $normalizedPath
            method = $methodName
            kind = $kind
            line = $index + 1
        })
    }

    return @($declarations)
}

function Assert-UniqueDeclarations {
    param(
        [Parameter(Mandatory)][object[]]$Declarations,
        [Parameter(Mandatory)][string]$Label
    )

    $duplicates = @($Declarations | Group-Object id | Where-Object Count -ne 1)
    if ($duplicates.Count -ne 0) {
        throw "$Label contains duplicate declaration ids: $($duplicates.Name -join ', ')"
    }
}

function Assert-PinnedBaseIdentity {
    param(
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$TestsTreeObject,
        [Parameter(Mandatory)][string]$DeclarationDigestSha256,
        [Parameter(Mandatory)][int]$DeclarationCount
    )

    if ($Commit -ne $pinnedBaseCommit -or
        $SourcePath -ne $pinnedBaseSourcePath -or
        $TestsTreeObject -ne $pinnedBaseTestsTreeObject -or
        $DeclarationDigestSha256 -ne $pinnedBaseDeclarationDigestSha256 -or
        $DeclarationCount -ne $pinnedBaseDeclarationCount) {
        throw "Cloud test migration source identity mismatch: commit=$Commit path=$SourcePath tree=$TestsTreeObject digest=$DeclarationDigestSha256 count=$DeclarationCount"
    }
}

function Assert-RejectedFixture {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        & $Action
    }
    catch {
        return $Name
    }

    throw "Cloud test migration negative fixture was not rejected: $Name"
}

function Assert-MappingDestination {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Disposition,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Targets,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Diagnostics,
        [Parameter(Mandatory)][hashtable]$CurrentById,
        [Parameter(Mandatory)][System.Collections.Generic.HashSet[string]]$AllowedDiagnostics
    )

    if ($Disposition -eq 'analyzer') {
        if ($Diagnostics.Count -eq 0 -or $Targets.Count -ne 0) {
            throw "Analyzer mapping must name diagnostics and no declaration targets: $Source"
        }
        foreach ($diagnostic in $Diagnostics) {
            if (-not $AllowedDiagnostics.Contains($diagnostic)) {
                throw "Unknown analyzer diagnostic '$diagnostic' for $Source."
            }
        }
        return
    }

    if ($Targets.Count -eq 0 -or $Diagnostics.Count -ne 0) {
        throw "$Disposition mapping must name current declaration targets and no diagnostics: $Source"
    }
    foreach ($target in $Targets) {
        if (-not $CurrentById.ContainsKey($target)) {
            throw "Migration target does not exist in current tree: source=$Source target=$target"
        }
    }
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "Missing Cloud test migration baseline: $BaselinePath"
}

$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
if ([int]$baseline.schemaVersion -ne 1) {
    throw "Unsupported Cloud test migration baseline schema: $($baseline.schemaVersion)"
}

$baseCommit = [string]$baseline.baseCommit
if ($baseCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Cloud test migration baseCommit must be a full lowercase SHA-1."
}
$baseSourcePath = [string]$baseline.baseSourcePath
$baseTestsTreeObject = [string]$baseline.baseTestsTreeObject
$baseDeclarationDigestSha256 = [string]$baseline.baseDeclarationDigestSha256
$expectedBaseDeclarationCount = [int]$baseline.expectedBaseDeclarationCount
$baseIdentity = @{
    Commit = $baseCommit
    SourcePath = $baseSourcePath
    TestsTreeObject = $baseTestsTreeObject
    DeclarationDigestSha256 = $baseDeclarationDigestSha256
    DeclarationCount = $expectedBaseDeclarationCount
}
Assert-PinnedBaseIdentity @baseIdentity

$negativeFixtures = [System.Collections.Generic.List[string]]::new()
$negativeFixtures.Add((Assert-RejectedFixture -Name 'source-commit' -Action {
    Assert-PinnedBaseIdentity ('0' * 40) $baseSourcePath $baseTestsTreeObject $baseDeclarationDigestSha256 $expectedBaseDeclarationCount
}))
$negativeFixtures.Add((Assert-RejectedFixture -Name 'source-path' -Action {
    Assert-PinnedBaseIdentity $baseCommit 'src/not-tests' $baseTestsTreeObject $baseDeclarationDigestSha256 $expectedBaseDeclarationCount
}))
$negativeFixtures.Add((Assert-RejectedFixture -Name 'source-tree' -Action {
    Assert-PinnedBaseIdentity $baseCommit $baseSourcePath ('0' * 40) $baseDeclarationDigestSha256 $expectedBaseDeclarationCount
}))
$negativeFixtures.Add((Assert-RejectedFixture -Name 'source-digest' -Action {
    Assert-PinnedBaseIdentity $baseCommit $baseSourcePath $baseTestsTreeObject ('0' * 64) $expectedBaseDeclarationCount
}))
$negativeFixtures.Add((Assert-RejectedFixture -Name 'source-count' -Action {
    Assert-PinnedBaseIdentity $baseCommit $baseSourcePath $baseTestsTreeObject $baseDeclarationDigestSha256 ($expectedBaseDeclarationCount + 1)
}))

$null = Invoke-GitText -Arguments @('cat-file', '-e', "$baseCommit^{commit}")
$null = Invoke-GitText -Arguments @('merge-base', '--is-ancestor', $baseCommit, 'HEAD')
$actualBaseTestsTreeObject = Invoke-GitText -Arguments @('rev-parse', "$baseCommit`:$baseSourcePath") |
    Select-Object -First 1
if ($actualBaseTestsTreeObject -ne $baseTestsTreeObject) {
    throw "Base src/tests tree object drifted: expected=$baseTestsTreeObject actual=$actualBaseTestsTreeObject"
}

$basePaths = @(Invoke-GitText -Arguments @('ls-tree', '-r', '--name-only', $baseCommit, '--', $baseSourcePath) |
    Where-Object { $_ -match '\.cs$' })
$baseDeclarations = [System.Collections.Generic.List[object]]::new()
foreach ($path in $basePaths) {
    $content = (Invoke-GitText -Arguments @('show', "$baseCommit`:$path")) -join "`n"
    foreach ($declaration in @(Get-TestDeclarations -Path $path -Content $content)) {
        $baseDeclarations.Add($declaration)
    }
}

$currentDeclarations = [System.Collections.Generic.List[object]]::new()
$currentRoot = Join-Path $repoRoot 'src/tests'
foreach ($file in @(Get-ChildItem $currentRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })) {
    $relativePath = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
    foreach ($declaration in @(Get-TestDeclarations -Path $relativePath -Content ([IO.File]::ReadAllText($file.FullName)))) {
        $currentDeclarations.Add($declaration)
    }
}

Assert-UniqueDeclarations -Declarations @($baseDeclarations) -Label 'Base test tree'
Assert-UniqueDeclarations -Declarations @($currentDeclarations) -Label 'Current test tree'

$baseDeclarationRows = [string[]]@($baseDeclarations | ForEach-Object { "$($_.id)|$($_.kind)" })
[Array]::Sort($baseDeclarationRows, [StringComparer]::Ordinal)
$baseDeclarationCanonical = ($baseDeclarationRows -join "`n") + "`n"
$actualBaseDeclarationDigestSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($baseDeclarationCanonical))).ToLowerInvariant()
if ($actualBaseDeclarationDigestSha256 -ne $baseDeclarationDigestSha256) {
    throw "Base declaration digest drifted: expected=$baseDeclarationDigestSha256 actual=$actualBaseDeclarationDigestSha256"
}
if ($baseDeclarations.Count -ne $expectedBaseDeclarationCount) {
    throw "Base test declaration count drifted: expected=$expectedBaseDeclarationCount actual=$($baseDeclarations.Count)"
}

$baseById = @{}
foreach ($declaration in $baseDeclarations) {
    $baseById[$declaration.id] = $declaration
}
$currentById = @{}
$currentByMethod = @{}
foreach ($declaration in $currentDeclarations) {
    $currentById[$declaration.id] = $declaration
    if (-not $currentByMethod.ContainsKey($declaration.method)) {
        $currentByMethod[$declaration.method] = [System.Collections.Generic.List[object]]::new()
    }
    $currentByMethod[$declaration.method].Add($declaration)
}

$mappingBySource = @{}
foreach ($mapping in @($baseline.mappings)) {
    $source = [string]$mapping.source
    if ([string]::IsNullOrWhiteSpace($source) -or $mappingBySource.ContainsKey($source)) {
        throw "Cloud test migration baseline contains a blank or duplicate source mapping: $source"
    }
    if (-not $baseById.ContainsKey($source)) {
        throw "Cloud test migration mapping source does not exist in base tree: $source"
    }
    $mappingBySource[$source] = $mapping
}

$allowedDiagnostics = [System.Collections.Generic.HashSet[string]]::new(
    [string[]](1..10 | ForEach-Object { 'CLOUDARCH{0:D3}' -f $_ }),
    [StringComparer]::Ordinal)
$negativeFixtures.Add((Assert-RejectedFixture -Name 'replacement-target' -Action {
    Assert-MappingDestination 'negative-source' 'replacement' @('src/tests/missing.cs::Missing') @() $currentById $allowedDiagnostics
}))
$negativeFixtures.Add((Assert-RejectedFixture -Name 'analyzer-diagnostic' -Action {
    Assert-MappingDestination 'negative-source' 'analyzer' @() @('CLOUDARCH999') $currentById $allowedDiagnostics
}))
$entries = [System.Collections.Generic.List[object]]::new()
$usedMappings = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($source in @($baseDeclarations | Sort-Object path, line)) {
    $disposition = $null
    $targets = @()
    $diagnostics = @()
    $reason = $null

    if ($currentById.ContainsKey($source.id)) {
        $disposition = 'retained'
        $targets = @($source.id)
        $reason = 'Exact declaration retained.'
    }
    else {
        $sameName = @(
            if ($currentByMethod.ContainsKey($source.method)) {
                $currentByMethod[$source.method]
            }
        )
        if ($sameName.Count -eq 1) {
            $disposition = 'moved'
            $targets = @([string]$sameName[0].id)
            $reason = 'Unique declaration moved to a physical test unit.'
        }
        elseif ($sameName.Count -gt 1) {
            throw "Ambiguous current declarations for $($source.id): $($sameName.id -join ', ')"
        }
        elseif ($mappingBySource.ContainsKey($source.id)) {
            $mapping = $mappingBySource[$source.id]
            $null = $usedMappings.Add($source.id)
            $disposition = [string]$mapping.disposition
            $targets = @(
                if ($mapping.PSObject.Properties['targets']) {
                    $mapping.targets | ForEach-Object { [string]$_ }
                }
            )
            $diagnostics = @(
                if ($mapping.PSObject.Properties['diagnostics']) {
                    $mapping.diagnostics | ForEach-Object { [string]$_ }
                }
            )
            $reason = [string]$mapping.reason

            if ($disposition -notin @('replacement', 'analyzer', 'retired-duplicate')) {
                throw "Unsupported disposition '$disposition' for $($source.id)."
            }
            if ([string]::IsNullOrWhiteSpace($reason)) {
                throw "Migration mapping must explain its semantic disposition: $($source.id)"
            }
            Assert-MappingDestination $source.id $disposition $targets $diagnostics $currentById $allowedDiagnostics
        }
        else {
            throw "Unmapped base test declaration: $($source.id)"
        }
    }

    $entries.Add([pscustomobject]@{
        source = [pscustomobject]@{
            id = $source.id
            path = $source.path
            method = $source.method
            kind = $source.kind
            line = $source.line
        }
        disposition = $disposition
        targets = @($targets)
        diagnostics = @($diagnostics)
        reason = $reason
    })
}

$unusedMappings = @($mappingBySource.Keys | Where-Object { -not $usedMappings.Contains($_) })
if ($unusedMappings.Count -ne 0) {
    throw "Migration baseline mappings became redundant or stale: $($unusedMappings -join ', ')"
}

$counts = [ordered]@{}
foreach ($disposition in @('retained', 'moved', 'replacement', 'analyzer', 'retired-duplicate')) {
    $counts[$disposition] = @($entries | Where-Object disposition -eq $disposition).Count
}
if (($counts.Values | Measure-Object -Sum).Sum -ne $baseDeclarations.Count) {
    throw "Cloud test migration ledger did not reconcile every base declaration."
}

$oldDeclarationTargets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in @($entries | Where-Object { $_.disposition -in @('retained', 'moved') })) {
    foreach ($target in @($entry.targets)) {
        $null = $oldDeclarationTargets.Add([string]$target)
    }
}
$currentDelta = @($currentDeclarations |
    Where-Object { -not $oldDeclarationTargets.Contains($_.id) } |
    Sort-Object path, line |
    ForEach-Object {
        [pscustomobject]@{
            id = $_.id
            path = $_.path
            method = $_.method
            kind = $_.kind
            line = $_.line
        }
    })

$artifact = [ordered]@{
    schemaVersion = 1
    baseCommit = $baseCommit
    baseSourcePath = $baseSourcePath
    baseTestsTreeObject = $actualBaseTestsTreeObject
    baseDeclarationDigestSha256 = $actualBaseDeclarationDigestSha256
    headCommit = (Invoke-GitText -Arguments @('rev-parse', 'HEAD') | Select-Object -First 1)
    baseDeclarationCount = $baseDeclarations.Count
    currentDeclarationCount = $currentDeclarations.Count
    currentDeltaCount = $currentDelta.Count
    unresolvedCount = 0
    negativeFixtureCount = $negativeFixtures.Count
    negativeFixtures = @($negativeFixtures)
    counts = $counts
    entries = @($entries)
    currentDelta = @($currentDelta)
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$artifact | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding utf8NoBOM

Write-Host (
    "CLOUD_TEST_MIGRATION_OK base={0} current={1} currentDelta={2} retained={3} moved={4} replacement={5} analyzer={6} retiredDuplicate={7} unresolved=0 negativeFixtures={8}" -f
    $baseDeclarations.Count,
    $currentDeclarations.Count,
    $currentDelta.Count,
    $counts.retained,
    $counts.moved,
    $counts.replacement,
    $counts.analyzer,
    $counts.'retired-duplicate',
    $negativeFixtures.Count)
