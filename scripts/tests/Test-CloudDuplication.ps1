[CmdletBinding()]
param(
    [string]$ReportDirectory = 'artifacts/test-results/quality/duplication',
    [string]$BaseRef = $env:CLOUD_QUALITY_BASE_REF,
    [switch]$UpdateFingerprintBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'CloudQualityBaselineProtection.ps1')
$baseCommit = Resolve-CloudQualityBaseCommit -RepoRoot $repoRoot -BaseRef $BaseRef
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-duplication.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
$fingerprintBaselinePath = Join-Path (Split-Path $baselinePath -Parent) ([string]$baseline.strictFingerprintBaseline)
$fingerprintBaseline = Get-Content $fingerprintBaselinePath -Raw | ConvertFrom-Json -Depth 100
$baseBaseline = Get-CloudQualityBaseJson `
    -RepoRoot $repoRoot `
    -BaseCommit $baseCommit `
    -RelativePath 'scripts/tests/baselines/cloud-duplication.json'
$baseFingerprintRelativePath = "scripts/tests/baselines/$([string]$baseBaseline.strictFingerprintBaseline)"
$baseFingerprintBaseline = Get-CloudQualityBaseJson `
    -RepoRoot $repoRoot `
    -BaseCommit $baseCommit `
    -RelativePath $baseFingerprintRelativePath
if ([int]$baseline.schemaVersion -lt [int]$baseBaseline.schemaVersion) {
    throw 'Cloud duplication baseline schema cannot be downgraded.'
}
foreach ($mode in @($baseBaseline.modes.PSObject.Properties.Name)) {
    if ($null -eq $baseline.modes.PSObject.Properties[$mode]) {
        throw "Cloud duplication candidate removes an existing scan mode: $mode"
    }
    Assert-CloudQualityAtMost ([int]$baseline.modes.$mode.minimumLines) ([int]$baseBaseline.modes.$mode.minimumLines) "duplication $mode minimumLines"
    Assert-CloudQualityAtMost ([int]$baseline.modes.$mode.minimumTokens) ([int]$baseBaseline.modes.$mode.minimumTokens) "duplication $mode minimumTokens"
}
foreach ($baseScan in @($baseBaseline.scans)) {
    if ([string]$baseScan.category -cne 'production') {
        continue
    }
    $candidateScans = @($baseline.scans | Where-Object {
        [string]$_.category -ceq [string]$baseScan.category -and
        [string]$_.mode -ceq [string]$baseScan.mode
    })
    if ($candidateScans.Count -ne 1) {
        throw "Cloud duplication candidate must retain exactly one existing scan: $($baseScan.category)/$($baseScan.mode)"
    }
    foreach ($metric in @('clones', 'duplicatedLines', 'duplicatedTokens')) {
        Assert-CloudQualityAtMost ([int]$candidateScans[0].$metric) ([int]$baseScan.$metric) "duplication $($baseScan.category)/$($baseScan.mode)/$metric"
    }
}
if (@($baseline.scans | Where-Object { [string]$_.category -cne 'production' }).Count -ne 0) {
    throw 'Cloud duplication Quality mode may scan production sources only; tests and test support are dynamic business assets.'
}

function Assert-FingerprintBaselineMonotonic($candidateFingerprint, [string]$label) {
    if ([int]$candidateFingerprint.schemaVersion -lt [int]$baseFingerprintBaseline.schemaVersion) {
        throw "$label strict fingerprint schema is downgraded."
    }
    foreach ($category in @('production')) {
        $baseCategory = $baseFingerprintBaseline.categories.$category
        $candidateCategory = $candidateFingerprint.categories.$category
        if ($null -eq $candidateCategory) {
            throw "$label strict fingerprint baseline is missing category $category."
        }
        Assert-CloudQualityAtMost ([int]$candidateCategory.groupCount) ([int]$baseCategory.groupCount) "$label $category strict group count"
        Assert-CloudQualityAtMost ([int]$candidateCategory.instanceCount) ([int]$baseCategory.instanceCount) "$label $category strict instance count"
    }
}

Assert-FingerprintBaselineMonotonic $fingerprintBaseline 'candidate'
$identityChurnFixture = @'
{"schemaVersion":3,"categories":{"production":{"groupCount":1,"instanceCount":1,"groups":[{"fingerprint":"new","instances":1}]}}}
'@ | ConvertFrom-Json -Depth 20
# Identity churn is legal when strict totals do not grow; actual observation is
# reconciled exactly against the candidate baseline below.
$savedBaseFingerprintBaseline = $baseFingerprintBaseline
try {
    $baseFingerprintBaseline = @'
{"schemaVersion":3,"categories":{"production":{"groupCount":1,"instanceCount":1,"groups":[{"fingerprint":"old","instances":1}]}}}
'@ | ConvertFrom-Json -Depth 20
    Assert-FingerprintBaselineMonotonic $identityChurnFixture 'identity-churn fixture'
}
finally {
    $baseFingerprintBaseline = $savedBaseFingerprintBaseline
}
$ghostFingerprintFixture = $null
try {
    Assert-CloudQualityExactSet -Candidate @('observed') -Base @('observed', 'ghost') -Label 'ghost fingerprint fixture'
}
catch {
    $ghostFingerprintFixture = $_.Exception.Message
}
if ($ghostFingerprintFixture -notmatch 'exact set changed') {
    throw "Strict fingerprint ghost fixture did not fail closed: $ghostFingerprintFixture"
}
$reportRoot = if ([System.IO.Path]::IsPathRooted($ReportDirectory)) {
    $ReportDirectory
} else {
    Join-Path $repoRoot $ReportDirectory
}
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

$versionOutput = @(& npx --yes "jscpd@$($baseline.version)" --version 2>&1)
if ($LASTEXITCODE -ne 0 -or ($versionOutput -join "`n") -notmatch "\b$([regex]::Escape([string]$baseline.version))\b") {
    throw "Expected jscpd $($baseline.version), got: $($versionOutput -join ' ')"
}

$commonIgnore = '**/bin/**,**/obj/**,**/Migrations/**,**/migrations/**,**/node_modules/**,**/dist/**,**/generated/**,**/*.g.cs,**/*.Designer.cs'
$productionInputs = @(
    'src/analyzers', 'src/core', 'src/hosts', 'src/infrastructure',
    'src/services', 'src/shared', 'src/ui/iiot-web/src'
)
$sourceIndexes = @{
    production = @{}
}

function Add-SourceIndexEntry(
    [hashtable]$index,
    [string]$reportName,
    [string]$sourcePath,
    [string]$category) {
    $normalizedName = $reportName.Replace('\', '/')
    if ($index.ContainsKey($normalizedName) -and
        -not [string]::Equals([string]$index[$normalizedName], $sourcePath, [StringComparison]::Ordinal)) {
        throw "jscpd report path is ambiguous for $category`: $normalizedName"
    }
    $index[$normalizedName] = $sourcePath
}

function Add-InputSources([string]$category, [string[]]$inputs) {
    $index = $sourceIndexes[$category]
    foreach ($input in $inputs) {
        $inputPath = Join-Path $repoRoot ($input -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (Test-Path $inputPath -PathType Container) {
            foreach ($file in Get-ChildItem $inputPath -File -Recurse) {
                if ($file.FullName -match '[\\/](bin|obj|Migrations|migrations|node_modules|dist|generated)[\\/]' -or
                    ($category -eq 'production' -and $file.Name -match '\.(test|spec)\.ts$')) {
                    continue
                }
                $relativeName = [System.IO.Path]::GetRelativePath($inputPath, $file.FullName).Replace('\', '/')
                Add-SourceIndexEntry $index $relativeName $file.FullName $category
            }
        } elseif (Test-Path $inputPath -PathType Leaf) {
            Add-SourceIndexEntry $index ([System.IO.Path]::GetFileName($inputPath)) $inputPath $category
            Add-SourceIndexEntry $index ([System.IO.Path]::GetRelativePath($repoRoot, $inputPath)) $inputPath $category
        } else {
            throw "Duplication input does not exist for $category`: $input"
        }
    }
}

Add-InputSources 'production' $productionInputs

function Get-Sha256([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-StrictGroupFingerprint([string]$category, $duplicate) {
    $sourceIndex = $sourceIndexes[$category]
    $sideHashes = [System.Collections.Generic.List[string]]::new()
    foreach ($side in @($duplicate.firstFile, $duplicate.secondFile)) {
        $reportName = (([string]$side.name).Replace('\', '/') -replace ':[a-z]+$', '')
        if (-not $sourceIndex.ContainsKey($reportName)) {
            throw "Unable to resolve jscpd source for strict fingerprint in $category`: $reportName"
        }
        $sourceLines = @(Get-Content $sourceIndex[$reportName])
        $start = [int]$side.start
        $end = [int]$side.end
        if ($start -lt 1 -or $end -lt $start -or $end -gt $sourceLines.Count) {
            throw "Invalid jscpd source span for $reportName`: $start..$end"
        }
        $fragment = @($sourceLines[($start - 1)..($end - 1)]) -join "`n"
        $normalized = [regex]::Replace($fragment, '\s+', ' ').Trim()
        $sideHashes.Add((Get-Sha256 $normalized))
    }
    $orderedSides = @($sideHashes | Sort-Object)
    return Get-Sha256 "$([string]$duplicate.format)|$($orderedSides -join '|')"
}

function Get-Inputs([string]$category) {
    switch ($category) {
        'production' { return $productionInputs }
        default { throw "Unknown duplication category: $category" }
    }
}

function Get-Format([string]$category) {
    return 'csharp,typescript,javascript,vue'
}

$results = [System.Collections.Generic.List[object]]::new()
$crossContextGroups = [System.Collections.Generic.List[object]]::new()
$strictFingerprintCounts = @{
    production = @{}
}
$strictFingerprintEvidence = @{
    production = @{}
}
foreach ($expected in $baseline.scans) {
    $category = [string]$expected.category
    $mode = [string]$expected.mode
    $modeSettings = $baseline.modes.$mode
    $scanDirectory = Join-Path $reportRoot "$category-$mode"
    Remove-Item $scanDirectory -Force -Recurse -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $scanDirectory -Force | Out-Null

    $arguments = @(
        '--yes', "jscpd@$($baseline.version)",
        '--reporters', 'json',
        '--output', $scanDirectory,
        '--mode', $mode,
        '--min-lines', [string]$modeSettings.minimumLines,
        '--min-tokens', [string]$modeSettings.minimumTokens,
        '--format', (Get-Format $category),
        '--ignore', $commonIgnore
    )
    if ($category -eq 'production') {
        $arguments += @('--ignore', "$commonIgnore,**/*.test.ts,**/*.spec.ts")
    }
    $arguments += @(Get-Inputs $category)
    $output = @(& npx @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "jscpd failed for $category/$mode`: $($output -join [Environment]::NewLine)"
    }

    $reportPath = Join-Path $scanDirectory 'jscpd-report.json'
    if (-not (Test-Path $reportPath -PathType Leaf)) {
        throw "Missing jscpd report for $category/$mode."
    }
    $report = Get-Content $reportPath -Raw | ConvertFrom-Json -Depth 100
    $metrics = [ordered]@{
        category = $category
        mode = $mode
        clones = [int]$report.statistics.total.clones
        duplicatedLines = [int]$report.statistics.total.duplicatedLines
        duplicatedTokens = [int]$report.statistics.total.duplicatedTokens
        lines = [int]$report.statistics.total.lines
        tokens = [int]$report.statistics.total.tokens
    }
    foreach ($metric in @('clones', 'duplicatedLines', 'duplicatedTokens')) {
        if ([int]$metrics[$metric] -ne [int]$expected.$metric) {
            throw "Duplication candidate baseline must exactly match the current $category/$mode/$metric report: baseline=$($expected.$metric) actual=$($metrics[$metric])"
        }
    }
    $results.Add($metrics)

    foreach ($duplicate in @($report.duplicates)) {
        if ($mode -eq 'strict') {
            $fingerprint = Get-StrictGroupFingerprint $category $duplicate
            $categoryCounts = $strictFingerprintCounts[$category]
            if (-not $categoryCounts.ContainsKey($fingerprint)) {
                $categoryCounts[$fingerprint] = 0
            }
            $categoryCounts[$fingerprint]++
            $categoryEvidence = $strictFingerprintEvidence[$category]
            if (-not $categoryEvidence.ContainsKey($fingerprint)) {
                $categoryEvidence[$fingerprint] = [System.Collections.Generic.List[string]]::new()
            }
            $categoryEvidence[$fingerprint].Add(
                "$([string]$duplicate.firstFile.name):$([int]$duplicate.firstFile.start) <-> $([string]$duplicate.secondFile.name):$([int]$duplicate.secondFile.start)")
        }
        if ($category -eq 'production') {
            $firstContext = ([string]$duplicate.firstFile.name -split '/')[0]
            $secondContext = ([string]$duplicate.secondFile.name -split '/')[0]
            if ($firstContext -ne $secondContext) {
                $crossContextGroups.Add([ordered]@{
                    mode = $mode
                    firstContext = $firstContext
                    firstFile = [string]$duplicate.firstFile.name
                    firstStart = [int]$duplicate.firstFile.start
                    secondContext = $secondContext
                    secondFile = [string]$duplicate.secondFile.name
                    secondStart = [int]$duplicate.secondFile.start
                    lines = [int]$duplicate.lines
                    tokens = [int]$duplicate.tokens
                })
            }
        }
    }
    Write-Host "CLOUD_DUPLICATION_SCAN_OK category=$category mode=$mode clones=$($metrics.clones) lines=$($metrics.duplicatedLines) tokens=$($metrics.duplicatedTokens)"
}

$strictCategorySummaries = [ordered]@{}
foreach ($category in @('production')) {
    $categoryCounts = $strictFingerprintCounts[$category]
    $strictGroups = @($categoryCounts.Keys | Sort-Object | ForEach-Object {
        [ordered]@{ fingerprint = $_; instances = [int]$categoryCounts[$_] }
    })
    $strictInstanceCount = 0
    foreach ($strictGroup in $strictGroups) {
        $strictInstanceCount += [int]$strictGroup.instances
    }
    $strictCategorySummaries[$category] = [ordered]@{
        groupCount = $strictGroups.Count
        instanceCount = $strictInstanceCount
        groups = $strictGroups
    }
}
if ($UpdateFingerprintBaseline) {
    $updatedFingerprintBaseline = [ordered]@{
        schemaVersion = 3
        mode = 'strict'
        normalization = 'source fragment whitespace collapsed; SHA-256 over format and ordered side hashes'
        categories = $strictCategorySummaries
    }
    Assert-FingerprintBaselineMonotonic $updatedFingerprintBaseline 'generated candidate'
    $updatedFingerprintBaseline | ConvertTo-Json -Depth 10 | Set-Content $fingerprintBaselinePath -Encoding utf8
} else {
    if (-not (Test-Path $fingerprintBaselinePath -PathType Leaf)) {
        throw "Missing strict clone fingerprint baseline: $fingerprintBaselinePath"
    }
    if ([int]$fingerprintBaseline.schemaVersion -ne 3) {
        throw 'Strict clone fingerprint baseline must use schemaVersion 3 with the production category.'
    }
    foreach ($category in @('production')) {
        $expectedCategory = $fingerprintBaseline.categories.$category
        if ($null -eq $expectedCategory) {
            throw "Strict clone fingerprint baseline is missing category $category."
        }
        $expectedGroups = @{}
        foreach ($group in @($expectedCategory.groups)) {
            if ($expectedGroups.ContainsKey([string]$group.fingerprint)) {
                throw "Strict clone fingerprint baseline has a duplicate $category fingerprint: $([string]$group.fingerprint)"
            }
            $expectedGroups[[string]$group.fingerprint] = [int]$group.instances
        }
        $categoryCounts = $strictFingerprintCounts[$category]
        Assert-CloudQualityExactSet `
            -Candidate @($categoryCounts.Keys) `
            -Base @($expectedGroups.Keys) `
            -Label "$category observed strict clone fingerprints"
        foreach ($fingerprint in $categoryCounts.Keys) {
            if ([int]$categoryCounts[$fingerprint] -ne [int]$expectedGroups[$fingerprint]) {
                throw "$category strict clone instances do not reconcile for group $fingerprint`: baseline=$($expectedGroups[$fingerprint]) actual=$($categoryCounts[$fingerprint]) evidence=$($strictFingerprintEvidence[$category][$fingerprint] -join '; ')"
            }
        }
        if ([int]$strictCategorySummaries[$category].groupCount -ne [int]$expectedCategory.groupCount -or
            [int]$strictCategorySummaries[$category].instanceCount -ne [int]$expectedCategory.instanceCount) {
            throw "$category strict clone totals do not reconcile: baseline=$($expectedCategory.groupCount)/$($expectedCategory.instanceCount) actual=$($strictCategorySummaries[$category].groupCount)/$($strictCategorySummaries[$category].instanceCount)"
        }
    }
}

$summaryPath = Join-Path $reportRoot 'cloud-duplication-summary.json'
[ordered]@{
    schemaVersion = 1
    tool = [string]$baseline.tool
    version = [string]$baseline.version
    scans = $results
    strictFingerprints = [ordered]@{
        categories = @($strictCategorySummaries.Keys | ForEach-Object {
            [ordered]@{
                category = $_
                groupFingerprints = [int]$strictCategorySummaries[$_].groupCount
                instances = [int]$strictCategorySummaries[$_].instanceCount
            }
        })
        fingerprintBaseline = [System.IO.Path]::GetRelativePath($repoRoot, $fingerprintBaselinePath).Replace('\', '/')
    }
    crossBoundedContextGroups = $crossContextGroups
    refactoringPolicy = 'report-only; no automatic shared package extraction'
} | ConvertTo-Json -Depth 30 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_DUPLICATION_OK scans=$($results.Count) crossContextGroups=$($crossContextGroups.Count) output=$summaryPath"
