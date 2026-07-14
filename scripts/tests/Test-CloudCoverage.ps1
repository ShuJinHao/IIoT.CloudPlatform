[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$BaseRef,
    [switch]$EstablishBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-coverage.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
if ([int]$baseline.schemaVersion -ne 2) {
    throw "Cloud coverage baseline schemaVersion must be 2, actual=$($baseline.schemaVersion)."
}
$resultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}
$indexPath = Join-Path $resultsRoot 'coverage/cloud-coverage-index.json'
if (-not (Test-Path $indexPath -PathType Leaf)) {
    throw "Missing coverage index: $indexPath"
}
$index = Get-Content $indexPath -Raw | ConvertFrom-Json -Depth 100

$propsSource = Get-Content (Join-Path $repoRoot 'src/tests/Directory.Build.props') -Raw
if (-not $propsSource.Contains("Include=`"$($baseline.collector)`"", [StringComparison]::Ordinal) -or
    -not $propsSource.Contains("Version=`"$($baseline.version)`"", [StringComparison]::Ordinal)) {
    throw "Coverage collector must be pinned to $($baseline.collector) $($baseline.version)."
}

$testManifest = Get-Content (Join-Path $repoRoot 'src/tests/cloud-test-inventory.json') -Raw | ConvertFrom-Json -Depth 100
$requiredAssemblies = @($testManifest.runners | Where-Object required | ForEach-Object { [string]$_.assembly } | Sort-Object)
$reportAssemblies = @($index.reports | ForEach-Object { [string]$_.assembly } | Sort-Object)
if ($requiredAssemblies.Count -ne [int]$baseline.requiredRunnerCount -or
    [int]$index.expectedReports -ne [int]$baseline.requiredRunnerCount -or
    $reportAssemblies.Count -ne [int]$baseline.requiredRunnerCount -or
    ($requiredAssemblies -join "`n") -ne ($reportAssemblies -join "`n")) {
    throw "Coverage aggregation must contain exactly the required $($baseline.requiredRunnerCount) runners."
}

function Get-CoverageMap([string]$reportPath) {
    [xml]$coverage = Get-Content $reportPath -Raw
    $map = @{}
    $packagesNode = $coverage.coverage.packages
    $packageNodes = if ($null -eq $packagesNode -or $null -eq $packagesNode.PSObject.Properties['package']) {
        @()
    } else {
        @($packagesNode.package)
    }
    foreach ($package in $packageNodes) {
        $packageName = [string]$package.name
        if ($packageName -match '(?i)(Tests|TestKit|Analyzers)$') {
            continue
        }
        if ($null -eq $package.classes -or $null -eq $package.classes.PSObject.Properties['class']) {
            continue
        }
        foreach ($class in @($package.classes.class)) {
            $filename = ([string]$class.filename).Replace('\', '/')
            if ($filename -notmatch '^(core|hosts|infrastructure|services|shared)/' -or
                $filename -match '(?i)/Migrations/') {
                continue
            }
            if ($null -eq $class.lines -or $null -eq $class.lines.PSObject.Properties['line']) {
                continue
            }
            foreach ($line in @($class.lines.line)) {
                $number = [int]$line.number
                $key = "${filename}:$number"
                $hits = [int]$line.hits
                $branchValid = 0
                $branchCovered = 0
                if ([string]$line.branch -eq 'True' -and
                    [string]$line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                    $branchCovered = [int]$Matches[1]
                    $branchValid = [int]$Matches[2]
                }
                if (-not $map.ContainsKey($key)) {
                    $map[$key] = [ordered]@{
                        filename = $filename
                        number = $number
                        hits = $hits
                        branchValid = $branchValid
                        branchCovered = $branchCovered
                    }
                } else {
                    $existing = $map[$key]
                    $existing.hits = [Math]::Max([int]$existing.hits, $hits)
                    $existing.branchValid = [Math]::Max([int]$existing.branchValid, $branchValid)
                    $existing.branchCovered = [Math]::Max([int]$existing.branchCovered, $branchCovered)
                }
            }
        }
    }
    return $map
}

function Get-Metrics($map) {
    $linesValid = 0
    $linesCovered = 0
    $branchesValid = 0
    $branchesCovered = 0
    foreach ($line in $map.Values) {
        $linesValid++
        if ([int]$line.hits -gt 0) {
            $linesCovered++
        }
        $branchesValid += [int]$line.branchValid
        $branchesCovered += [int]$line.branchCovered
    }
    return [ordered]@{
        linesValid = $linesValid
        linesCovered = $linesCovered
        branchesValid = $branchesValid
        branchesCovered = $branchesCovered
        lineRate = if ($linesValid -eq 0) { 1.0 } else { [Math]::Round($linesCovered / $linesValid, 8) }
        branchRate = if ($branchesValid -eq 0) { 1.0 } else { [Math]::Round($branchesCovered / $branchesValid, 8) }
    }
}

function Get-SourceUniverse($map) {
    $files = @(
        $map.Values |
            Group-Object { [string]$_['filename'] } |
            Sort-Object Name |
            ForEach-Object {
                $fileLines = @($_.Group)
                $fileBranches = 0
                foreach ($fileLine in $fileLines) {
                    $fileBranches += [int]$fileLine['branchValid']
                }
                [pscustomobject][ordered]@{
                    path = [string]$_.Name
                    linesValid = $fileLines.Count
                    branchesValid = $fileBranches
                }
            }
    )
    $canonical = @($files | ForEach-Object {
        "$($_.path)`n$($_.linesValid)`n$($_.branchesValid)"
    }) -join "`n"
    $digestBytes = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($canonical))
    return [ordered]@{
        fileCount = $files.Count
        linesValid = [int](($files | Measure-Object linesValid -Sum).Sum)
        branchesValid = [int](($files | Measure-Object branchesValid -Sum).Sum)
        fileMetricsSha256 = [Convert]::ToHexString($digestBytes).ToLowerInvariant()
    }
}

function Test-SourceUniverseFingerprint {
    $complete = @{
        'a.cs:1' = [ordered]@{ filename = 'a.cs'; number = 1; hits = 1; branchValid = 0; branchCovered = 0 }
        'b.cs:1' = [ordered]@{ filename = 'b.cs'; number = 1; hits = 0; branchValid = 2; branchCovered = 0 }
    }
    $missingFile = @{
        'a.cs:1' = $complete['a.cs:1']
    }
    $completeUniverse = Get-SourceUniverse $complete
    $missingUniverse = Get-SourceUniverse $missingFile
    if ([int]$completeUniverse.fileCount -ne 2 -or
        [int]$completeUniverse.linesValid -ne 2 -or
        [int]$completeUniverse.branchesValid -ne 2 -or
        [string]$completeUniverse.fileMetricsSha256 -ceq [string]$missingUniverse.fileMetricsSha256) {
        throw 'Coverage source-universe omission fixture did not change the fingerprint.'
    }
}

Test-SourceUniverseFingerprint

$mergedMap = @{}
$runnerMetrics = [System.Collections.Generic.List[object]]::new()
foreach ($reportEntry in @($index.reports | Sort-Object assembly)) {
    $reportPath = Join-Path $resultsRoot (([string]$reportEntry.path) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $reportPath -PathType Leaf)) {
        throw "Coverage report is missing for $($reportEntry.assembly): $reportPath"
    }
    $runnerMap = Get-CoverageMap $reportPath
    $metrics = Get-Metrics $runnerMap
    $runnerMetrics.Add([ordered]@{ assembly = [string]$reportEntry.assembly; metrics = $metrics })
    foreach ($key in $runnerMap.Keys) {
        $line = $runnerMap[$key]
        if (-not $mergedMap.ContainsKey($key)) {
            $mergedMap[$key] = [ordered]@{
                filename = [string]$line.filename
                number = [int]$line.number
                hits = [int]$line.hits
                branchValid = [int]$line.branchValid
                branchCovered = [int]$line.branchCovered
            }
        } else {
            $existing = $mergedMap[$key]
            $existing.hits = [Math]::Max([int]$existing.hits, [int]$line.hits)
            $existing.branchValid = [Math]::Max([int]$existing.branchValid, [int]$line.branchValid)
            $existing.branchCovered = [Math]::Max([int]$existing.branchCovered, [int]$line.branchCovered)
        }
    }
}
$mergedMetrics = Get-Metrics $mergedMap
$sourceUniverse = Get-SourceUniverse $mergedMap

if (-not $EstablishBaseline) {
    if ([double]$mergedMetrics.lineRate + 0.000000001 -lt [double]$baseline.merged.lineRate -or
        [double]$mergedMetrics.branchRate + 0.000000001 -lt [double]$baseline.merged.branchRate) {
        throw "Coverage regressed: baseline line=$($baseline.merged.lineRate) branch=$($baseline.merged.branchRate), actual line=$($mergedMetrics.lineRate) branch=$($mergedMetrics.branchRate)."
    }
    if ([int]$sourceUniverse.fileCount -ne [int]$baseline.sourceUniverse.fileCount -or
        [int]$sourceUniverse.linesValid -ne [int]$baseline.sourceUniverse.linesValid -or
        [int]$sourceUniverse.branchesValid -ne [int]$baseline.sourceUniverse.branchesValid -or
        [string]$sourceUniverse.fileMetricsSha256 -cne [string]$baseline.sourceUniverse.fileMetricsSha256) {
        throw "Coverage source universe changed without an explicit baseline update: baseline files=$($baseline.sourceUniverse.fileCount) lines=$($baseline.sourceUniverse.linesValid) branches=$($baseline.sourceUniverse.branchesValid) digest=$($baseline.sourceUniverse.fileMetricsSha256); actual files=$($sourceUniverse.fileCount) lines=$($sourceUniverse.linesValid) branches=$($sourceUniverse.branchesValid) digest=$($sourceUniverse.fileMetricsSha256)."
    }
}

$newCodeMetrics = [ordered]@{
    baseRef = $BaseRef
    comparison = $null
    changedFiles = 0
    filesPresentInCoverage = 0
    allowlistedNonExecutableFiles = @()
    addedLines = 0
    executableLines = 0
    coveredLines = 0
    lineRate = $null
    branchesValid = 0
    branchesCovered = 0
    branchRate = $null
    status = 'BaseRefRequired'
}
if (-not [string]::IsNullOrWhiteSpace($BaseRef)) {
    # A clean CI checkout compares PR base to committed HEAD. A local dirty checkout compares
    # the same base to the complete tracked working tree so uncommitted P0 additions cannot leak.
    $diffOutput = @(& git -C $repoRoot diff --unified=0 --no-color $BaseRef -- src/core src/services 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to calculate P0 new-code diff from '$BaseRef': $($diffOutput -join [Environment]::NewLine)"
    }
    $addedKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $changedFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $currentFile = $null
    foreach ($line in $diffOutput) {
        $text = [string]$line
        if ($text -eq '+++ /dev/null') {
            $currentFile = $null
            continue
        }
        if ($text -match '^\+\+\+ b/(src/(core|services)/.+\.cs)$') {
            $currentFile = $Matches[1].Substring(4).Replace('\', '/')
            $null = $changedFiles.Add($Matches[1].Replace('\', '/'))
            continue
        }
        if ($null -ne $currentFile -and $text -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            $start = [int]$Matches[1]
            $count = if ([string]::IsNullOrWhiteSpace($Matches[2])) { 1 } else { [int]$Matches[2] }
            for ($offset = 0; $offset -lt $count; $offset++) {
                $null = $addedKeys.Add("${currentFile}:$($start + $offset)")
            }
        }
    }
    $untrackedFiles = @(& git -C $repoRoot ls-files --others --exclude-standard -- src/core src/services |
        Where-Object { $_ -match '^src/(core|services)/.+\.cs$' })
    foreach ($untrackedFile in $untrackedFiles) {
        $relativeFile = ([string]$untrackedFile).Substring(4).Replace('\', '/')
        $null = $changedFiles.Add(([string]$untrackedFile).Replace('\', '/'))
        $sourceLines = @(Get-Content (Join-Path $repoRoot ([string]$untrackedFile)))
        for ($lineNumber = 1; $lineNumber -le $sourceLines.Count; $lineNumber++) {
            $null = $addedKeys.Add("${relativeFile}:$lineNumber")
        }
    }
    $newCodeMetrics.comparison = 'base-ref-to-working-tree (clean CI equals base-ref-to-HEAD)'
    $newCodeMetrics.changedFiles = $changedFiles.Count
    $newCodeMetrics.addedLines = $addedKeys.Count

    $coverageFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($coverageLine in $mergedMap.Values) {
        $null = $coverageFiles.Add("src/$([string]$coverageLine.filename)")
    }
    $nonExecutableAllowlist = @{}
    foreach ($allowlistEntry in @($baseline.newP0Code.nonExecutableFileAllowlist)) {
        $allowlistPath = ([string]$allowlistEntry.path).Replace('\', '/')
        if ($allowlistPath -notmatch '^src/(core|services)/.+\.cs$' -or
            [string]$allowlistEntry.classification -ne 'non-executable-source' -or
            [string]::IsNullOrWhiteSpace([string]$allowlistEntry.reason)) {
            throw "Invalid P0 non-executable source allowlist entry: $allowlistPath"
        }
        $nonExecutableAllowlist[$allowlistPath] = [string]$allowlistEntry.reason
    }
    $missingCoverageFiles = [System.Collections.Generic.List[string]]::new()
    $allowlistedMissingFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($changedFile in @($changedFiles | Sort-Object)) {
        if ($coverageFiles.Contains($changedFile)) {
            $newCodeMetrics.filesPresentInCoverage++
        } elseif ($nonExecutableAllowlist.ContainsKey($changedFile)) {
            $allowlistedMissingFiles.Add([ordered]@{
                path = $changedFile
                classification = 'non-executable-source'
                reason = $nonExecutableAllowlist[$changedFile]
            })
        } else {
            $missingCoverageFiles.Add($changedFile)
        }
    }
    $newCodeMetrics.allowlistedNonExecutableFiles = $allowlistedMissingFiles
    if ($missingCoverageFiles.Count -gt 0) {
        throw "Changed P0 source files are absent from every coverage report and have no precise non-executable allowlist entry: $($missingCoverageFiles -join ', ')"
    }

    $newCodeMetrics.status = if ($addedKeys.Count -eq 0) { 'NoP0SourceAdditions' } else { 'NoExecutableP0Additions' }
    $newLines = @($addedKeys | Where-Object { $mergedMap.ContainsKey($_) } | ForEach-Object { $mergedMap[$_] })
    if ($newLines.Count -gt 0) {
        $newCodeMetrics.executableLines = $newLines.Count
        $newCodeMetrics.coveredLines = @($newLines | Where-Object { [int]$_.hits -gt 0 }).Count
        $newCodeMetrics.lineRate = [Math]::Round($newCodeMetrics.coveredLines / $newCodeMetrics.executableLines, 8)
        foreach ($newLine in $newLines) {
            $newCodeMetrics.branchesValid += [int]$newLine.branchValid
            $newCodeMetrics.branchesCovered += [int]$newLine.branchCovered
        }
        $newCodeMetrics.branchRate = if ($newCodeMetrics.branchesValid -eq 0) { 1.0 } else {
            [Math]::Round($newCodeMetrics.branchesCovered / $newCodeMetrics.branchesValid, 8)
        }
        if ([double]$newCodeMetrics.lineRate -lt [double]$baseline.newP0Code.minimumLineRate -or
            [double]$newCodeMetrics.branchRate -lt [double]$baseline.newP0Code.minimumBranchRate) {
            throw "P0 new-code coverage failed: line=$($newCodeMetrics.lineRate) branch=$($newCodeMetrics.branchRate)."
        }
        $newCodeMetrics.status = 'Passed'
    }
} elseif (-not $EstablishBaseline) {
    throw 'BaseRef is required for the P0 new-code coverage gate.'
}

$summaryPath = Join-Path $resultsRoot 'coverage/cloud-coverage-summary.json'
[ordered]@{
    schemaVersion = 1
    collector = [string]$baseline.collector
    version = [string]$baseline.version
    requiredRunners = $requiredAssemblies.Count
    reports = $reportAssemblies.Count
    merged = $mergedMetrics
    sourceUniverse = $sourceUniverse
    runners = $runnerMetrics
    newP0Code = $newCodeMetrics
} | ConvertTo-Json -Depth 20 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_COVERAGE_OK runners=$($requiredAssemblies.Count) reports=$($reportAssemblies.Count) lines=$($mergedMetrics.linesCovered)/$($mergedMetrics.linesValid) branches=$($mergedMetrics.branchesCovered)/$($mergedMetrics.branchesValid) lineRate=$($mergedMetrics.lineRate) branchRate=$($mergedMetrics.branchRate) newP0=$($newCodeMetrics.status) output=$summaryPath"
