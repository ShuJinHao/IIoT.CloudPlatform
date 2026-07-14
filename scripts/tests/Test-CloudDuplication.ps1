[CmdletBinding()]
param(
    [string]$ReportDirectory = 'artifacts/test-results/quality/duplication',
    [switch]$UpdateFingerprintBaseline,
    [switch]$AuditGrowth
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-duplication.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
$fingerprintBaselinePath = Join-Path (Split-Path $baselinePath -Parent) ([string]$baseline.strictFingerprintBaseline)
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
$supportInputs = @('src/testing')
$webTestInputs = @(Get-ChildItem (Join-Path $repoRoot 'src/ui/iiot-web') -File -Recurse |
    Where-Object { $_.Name -match '\.(test|spec)\.ts$' } |
    Where-Object { $_.FullName -notmatch '[\\/](node_modules|dist|test-results|playwright-report)[\\/]' } |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$testInputs = @('src/tests') + $webTestInputs

$sourceIndexes = @{
    production = @{}
    support = @{}
    'test-cases' = @{}
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
Add-InputSources 'support' $supportInputs
Add-InputSources 'test-cases' $testInputs

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
        'support' { return $supportInputs }
        'test-cases' { return $testInputs }
        default { throw "Unknown duplication category: $category" }
    }
}

function Get-Format([string]$category) {
    if ($category -eq 'support') {
        return 'csharp'
    }
    return 'csharp,typescript,javascript,vue'
}

$results = [System.Collections.Generic.List[object]]::new()
$crossContextGroups = [System.Collections.Generic.List[object]]::new()
$strictFingerprintCounts = @{
    production = @{}
    support = @{}
    'test-cases' = @{}
}
$strictFingerprintEvidence = @{
    production = @{}
    support = @{}
    'test-cases' = @{}
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
        if (-not $UpdateFingerprintBaseline -and -not $AuditGrowth -and [int]$metrics[$metric] -gt [int]$expected.$metric) {
            throw "Duplication ratchet grew for $category/$mode/$metric`: baseline=$($expected.$metric) actual=$($metrics[$metric])"
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
foreach ($category in @('production', 'support', 'test-cases')) {
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
    [ordered]@{
        schemaVersion = 2
        mode = 'strict'
        normalization = 'source fragment whitespace collapsed; SHA-256 over format and ordered side hashes'
        categories = $strictCategorySummaries
    } | ConvertTo-Json -Depth 10 | Set-Content $fingerprintBaselinePath -Encoding utf8
} else {
    if (-not (Test-Path $fingerprintBaselinePath -PathType Leaf)) {
        throw "Missing strict clone fingerprint baseline: $fingerprintBaselinePath"
    }
    $fingerprintBaseline = Get-Content $fingerprintBaselinePath -Raw | ConvertFrom-Json -Depth 100
    if ([int]$fingerprintBaseline.schemaVersion -ne 2) {
        throw 'Strict clone fingerprint baseline must use schemaVersion 2 with all categories.'
    }
    foreach ($category in @('production', 'support', 'test-cases')) {
        $expectedCategory = $fingerprintBaseline.categories.$category
        if ($null -eq $expectedCategory) {
            throw "Strict clone fingerprint baseline is missing category $category."
        }
        $expectedGroups = @{}
        foreach ($group in @($expectedCategory.groups)) {
            $expectedGroups[[string]$group.fingerprint] = [int]$group.instances
        }
        $categoryCounts = $strictFingerprintCounts[$category]
        $newGroups = @($categoryCounts.Keys | Where-Object { -not $expectedGroups.ContainsKey($_) })
        if ($newGroups.Count -gt 0) {
            if (-not $AuditGrowth) {
                throw "New $category strict clone groups are forbidden: $($newGroups -join ', ')"
            }
            foreach ($fingerprint in $newGroups) {
                Write-Warning "New $category strict clone group $fingerprint`: $($strictFingerprintEvidence[$category][$fingerprint] -join '; ')"
            }
        }
        foreach ($fingerprint in $categoryCounts.Keys) {
            if ([int]$categoryCounts[$fingerprint] -gt [int]$expectedGroups[$fingerprint]) {
                if (-not $AuditGrowth) {
                    throw "$category strict clone instances grew for group $fingerprint`: baseline=$($expectedGroups[$fingerprint]) actual=$($categoryCounts[$fingerprint])"
                }
                Write-Warning "$category strict clone instances grew for group $fingerprint`: baseline=$($expectedGroups[$fingerprint]) actual=$($categoryCounts[$fingerprint]) evidence=$($strictFingerprintEvidence[$category][$fingerprint] -join '; ')"
            }
        }
        if ([int]$strictCategorySummaries[$category].groupCount -gt [int]$expectedCategory.groupCount) {
            if (-not $AuditGrowth) {
                throw "$category strict clone group count grew: baseline=$($expectedCategory.groupCount) actual=$($strictCategorySummaries[$category].groupCount)"
            }
            Write-Warning "$category strict clone group count grew: baseline=$($expectedCategory.groupCount) actual=$($strictCategorySummaries[$category].groupCount)"
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
