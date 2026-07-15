[CmdletBinding()]
param(
    [string]$ReportDirectory = 'artifacts/test-results/quality',
    [string]$EdgeRepositoryRoot = $env:EDGECLIENT_REPOSITORY_ROOT,
    [string]$BaseRef = $env:CLOUD_QUALITY_BASE_REF
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'CloudQualityBaselineProtection.ps1')
$baseCommit = Resolve-CloudQualityBaseCommit -RepoRoot $repoRoot -BaseRef $BaseRef
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-compatibility.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
if ([int]$baseline.schemaVersion -ne 3 -or [int]$baseline.externalConsumerEvidenceCount -le 0) {
    throw 'Cloud compatibility baseline must use schemaVersion=3 and declare a positive externalConsumerEvidenceCount.'
}
$baseBaseline = Get-CloudQualityBaseJson `
    -RepoRoot $repoRoot `
    -BaseCommit $baseCommit `
    -RelativePath 'scripts/tests/baselines/cloud-compatibility.json'
if ([int]$baseBaseline.schemaVersion -lt 2 -or [int]$baseline.schemaVersion -lt [int]$baseBaseline.schemaVersion) {
    throw "Cloud compatibility baseline schema cannot be downgraded: base=$($baseBaseline.schemaVersion) candidate=$($baseline.schemaVersion)."
}
Assert-CloudQualityAtMost ([int]$baseline.activeCompatibilityItems) ([int]$baseBaseline.activeCompatibilityItems) 'active compatibility item count'
Assert-CloudQualityAtMost ([int]$baseline.unclassifiedCompatibilitySignals) ([int]$baseBaseline.unclassifiedCompatibilitySignals) 'unclassified compatibility signal count'

function Get-InventoryEntryById($entries, [string]$id, [string]$label) {
    $matches = @($entries | Where-Object { [string]$_.id -ceq $id })
    if ($matches.Count -gt 1) {
        throw "$label contains duplicate id '$id'."
    }
    if ($matches.Count -eq 1) {
        return $matches[0]
    }
    return $null
}

function Assert-InventoryInvariant([string]$id, $baseEntry, $candidateEntry, [string[]]$properties) {
    foreach ($propertyPath in $properties) {
        $baseValue = $baseEntry
        $candidateValue = $candidateEntry
        foreach ($segment in $propertyPath.Split('.')) {
            $baseValue = $baseValue.$segment
            $candidateValue = $candidateValue.$segment
        }
        if ([string]$candidateValue -cne [string]$baseValue) {
            throw "Compatibility inventory '$id' rewrites immutable field '$propertyPath': base='$baseValue' candidate='$candidateValue'."
        }
    }
}

$candidateRetainedIds = @($baseline.retainedCompatibility | ForEach-Object { [string]$_.id })
$candidateRetiredIds = @($baseline.retired | ForEach-Object { [string]$_.id })
foreach ($baseEntry in @($baseBaseline.retainedCompatibility)) {
    $id = [string]$baseEntry.id
    $candidateEntry = Get-InventoryEntryById $baseline.retainedCompatibility $id 'candidate retainedCompatibility'
    if ($null -eq $candidateEntry) {
        if ($id -notin $candidateRetiredIds) {
            throw "Immutable-base compatibility item '$id' disappeared without a physical-retirement inventory record."
        }
        continue
    }
    Assert-InventoryInvariant $id $baseEntry $candidateEntry @(
        'classification',
        'disposition',
        'producer.path',
        'producer.pattern',
        'candidateEvidence.path',
        'candidateEvidence.pattern'
    )
}
$newRetainedIds = @($candidateRetainedIds | Where-Object {
    $id = $_
    $null -eq (Get-InventoryEntryById $baseBaseline.retainedCompatibility $id 'immutable retainedCompatibility')
})
if ($newRetainedIds.Count -gt 0) {
    throw "New retained compatibility items cannot be self-authorized by this baseline: $($newRetainedIds -join ', ')."
}
foreach ($baseRetired in @($baseBaseline.retired)) {
    if ([string]$baseRetired.id -notin $candidateRetiredIds) {
        throw "Retired compatibility item was resurrected or removed from inventory: $($baseRetired.id)"
    }
}
$baseOrdinaryIds = @($baseBaseline.retainedOrdinaryAbstractions | ForEach-Object { [string]$_.id })
$candidateOrdinaryIds = @($baseline.retainedOrdinaryAbstractions | ForEach-Object { [string]$_.id })
$newOrdinaryIds = @($candidateOrdinaryIds | Where-Object { $_ -notin $baseOrdinaryIds })
if ($newOrdinaryIds.Count -gt 0) {
    throw "New ordinary-abstraction classifications cannot self-authorize compatibility candidates: $($newOrdinaryIds -join ', ')."
}
foreach ($baseEntry in @($baseBaseline.retainedOrdinaryAbstractions)) {
    $candidateEntry = Get-InventoryEntryById $baseline.retainedOrdinaryAbstractions ([string]$baseEntry.id) 'candidate retainedOrdinaryAbstractions'
    if ($null -eq $candidateEntry) {
        throw "Immutable-base ordinary abstraction disappeared without an independent contract-retirement batch: $($baseEntry.id)"
    }
    Assert-InventoryInvariant ([string]$baseEntry.id) $baseEntry $candidateEntry @(
        'classification',
        'disposition',
        'producer.path',
        'producer.pattern'
    )
}
if ($null -ne $baseBaseline.PSObject.Properties['externalConsumerEvidenceCount']) {
    Assert-CloudQualityAtLeast ([int]$baseline.externalConsumerEvidenceCount) ([int]$baseBaseline.externalConsumerEvidenceCount) 'external compatibility consumer evidence count'
}
$reportRoot = if ([System.IO.Path]::IsPathRooted($ReportDirectory)) {
    $ReportDirectory
} else {
    Join-Path $repoRoot $ReportDirectory
}
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

function Resolve-RepoPath([string]$relativePath) {
    return Join-Path $repoRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-CodeStartMask([string]$source) {
    $mask = [bool[]]::new($source.Length)
    $state = 'code'
    $rawDelimiterLength = 0
    $index = 0
    while ($index -lt $source.Length) {
        $current = $source[$index]
        $next = if ($index + 1 -lt $source.Length) { $source[$index + 1] } else { [char]0 }

        switch ($state) {
            'code' {
                if ($current -eq '/' -and $next -eq '/') {
                    $state = 'line-comment'
                    $index += 2
                    continue
                }
                if ($current -eq '/' -and $next -eq '*') {
                    $state = 'block-comment'
                    $index += 2
                    continue
                }
                if ($current -eq '"') {
                    $quoteCount = 1
                    while ($index + $quoteCount -lt $source.Length -and
                           $source[$index + $quoteCount] -eq '"') {
                        $quoteCount++
                    }
                    if ($quoteCount -ge 3) {
                        $state = 'raw-string'
                        $rawDelimiterLength = $quoteCount
                        $index += $quoteCount
                        continue
                    }
                    $isVerbatim = ($index -ge 1 -and $source[$index - 1] -eq '@') -or
                        ($index -ge 2 -and $source[$index - 1] -eq '$' -and $source[$index - 2] -eq '@')
                    $state = if ($isVerbatim) { 'verbatim-string' } else { 'double-string' }
                    $index++
                    continue
                }
                if ($current -eq "'") {
                    $state = 'single-string'
                    $index++
                    continue
                }
                if ($current -eq [char]96) {
                    $state = 'template-string'
                    $index++
                    continue
                }
                $mask[$index] = $true
                $index++
            }
            'line-comment' {
                if ($current -eq "`r" -or $current -eq "`n") {
                    $state = 'code'
                    $mask[$index] = $true
                }
                $index++
            }
            'block-comment' {
                if ($current -eq '*' -and $next -eq '/') {
                    $state = 'code'
                    $index += 2
                    continue
                }
                $index++
            }
            'double-string' {
                if ($current -eq '\\') {
                    $index = [Math]::Min($index + 2, $source.Length)
                    continue
                }
                if ($current -eq '"') { $state = 'code' }
                $index++
            }
            'single-string' {
                if ($current -eq '\\') {
                    $index = [Math]::Min($index + 2, $source.Length)
                    continue
                }
                if ($current -eq "'") { $state = 'code' }
                $index++
            }
            'template-string' {
                if ($current -eq '\\') {
                    $index = [Math]::Min($index + 2, $source.Length)
                    continue
                }
                if ($current -eq [char]96) { $state = 'code' }
                $index++
            }
            'verbatim-string' {
                if ($current -eq '"' -and $next -eq '"') {
                    $index += 2
                    continue
                }
                if ($current -eq '"') { $state = 'code' }
                $index++
            }
            'raw-string' {
                if ($current -eq '"') {
                    $quoteCount = 1
                    while ($index + $quoteCount -lt $source.Length -and
                           $source[$index + $quoteCount] -eq '"') {
                        $quoteCount++
                    }
                    if ($quoteCount -ge $rawDelimiterLength) {
                        $state = 'code'
                        $index += $rawDelimiterLength
                        continue
                    }
                }
                $index++
            }
        }
    }
    return ,$mask
}

function Get-CodePatternCount([string]$source, [string]$pattern) {
    if ([string]::IsNullOrEmpty($pattern) -or $source.Length -eq 0) {
        return 0
    }
    $mask = Get-CodeStartMask $source
    $count = 0
    $searchFrom = 0
    while ($searchFrom -lt $source.Length) {
        $matchIndex = $source.IndexOf($pattern, $searchFrom, [StringComparison]::Ordinal)
        if ($matchIndex -lt 0) { break }
        if ($mask[$matchIndex]) { $count++ }
        $searchFrom = $matchIndex + [Math]::Max(1, $pattern.Length)
    }
    return $count
}

$codeEvidenceFixture = @'
// RuntimePort.ExecuteAsync()
var decoy = "RuntimePort.ExecuteAsync()";
await RuntimePort.ExecuteAsync();
'@
if ((Get-CodePatternCount $codeEvidenceFixture 'RuntimePort.ExecuteAsync()') -ne 1 -or
    (Get-CodePatternCount '/* RuntimePort.ExecuteAsync() */' 'RuntimePort.ExecuteAsync()') -ne 0) {
    throw 'Compatibility executable-code evidence fixture failed to reject comment/string decoys.'
}

function Assert-FileEvidence($evidence, [string]$label) {
    $path = Resolve-RepoPath ([string]$evidence.path)
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "$label evidence path does not exist: $($evidence.path)"
    }
    $source = Get-Content $path -Raw
    $isConfiguration = [System.IO.Path]::GetExtension($path) -in @('.json', '.yml', '.yaml', '.conf')
    $matchCount = if ($isConfiguration) {
        [regex]::Matches($source, [regex]::Escape([string]$evidence.pattern)).Count
    } else {
        Get-CodePatternCount $source ([string]$evidence.pattern)
    }
    if ($matchCount -eq 0) {
        throw "$label evidence pattern is missing from $($evidence.path): $($evidence.pattern)"
    }
}

function Get-TextSha256([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

if ([string]::IsNullOrWhiteSpace($EdgeRepositoryRoot)) {
    throw 'EdgeRepositoryRoot is required. Compatibility evidence cannot be verified from a missing or inferred external worktree.'
}
$edgeRoot = (Resolve-Path $EdgeRepositoryRoot).Path
if (-not (Test-Path (Join-Path $edgeRoot 'IIoT.EdgeClient.slnx') -PathType Leaf)) {
    throw "Edge evidence root does not contain IIoT.EdgeClient.slnx: $edgeRoot"
}

$edgeHeadOutput = @(& git -C $edgeRoot rev-parse HEAD 2>&1)
if ($LASTEXITCODE -ne 0 -or ($edgeHeadOutput -join '').Trim() -notmatch '^[0-9a-f]{40}$') {
    throw "Unable to resolve the Edge evidence HEAD: $($edgeHeadOutput -join ' ')"
}
$edgeHead = ($edgeHeadOutput -join '').Trim()

$edgeStatus = @(& git -C $edgeRoot status --porcelain --untracked-files=all 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the Edge evidence worktree state: $($edgeStatus -join ' ')"
}
if ($edgeStatus.Count -ne 0) {
    throw "Edge evidence worktree must be clean; external HEAD/source hashes cannot describe dirty sources: $($edgeStatus -join ', ')"
}
$edgeRepositoryClean = $true

$scanRoots = @(
    'src/core',
    'src/hosts',
    'src/infrastructure',
    'src/services',
    'src/shared',
    'src/testing',
    'src/ui/iiot-web/src',
    'deploy/nginx/nginx.conf'
)
$scanFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($relativeRoot in $scanRoots) {
    $root = Resolve-RepoPath $relativeRoot
    if (Test-Path $root -PathType Leaf) {
        $scanFiles.Add((Get-Item $root))
        continue
    }
    if (-not (Test-Path $root -PathType Container)) {
        throw "Compatibility scan root is missing: $relativeRoot"
    }
    foreach ($file in Get-ChildItem $root -File -Recurse) {
        if ($file.FullName -match '[\\/](bin|obj|Migrations|node_modules|dist)[\\/]') {
            continue
        }
        if ($file.Extension -notin @('.cs', '.json', '.ts', '.vue', '.yml', '.yaml', '.conf')) {
            continue
        }
        if ($file.Name -match '\.(test|spec)\.ts$') {
            continue
        }
        $scanFiles.Add($file)
    }
}

$retiredCount = 0
foreach ($retired in $baseline.retired) {
    foreach ($property in @('producer', 'consumerEvidence', 'replacement', 'deletionCondition', 'latestRemovalBatch')) {
        if ($null -eq $retired.PSObject.Properties[$property] -or [string]::IsNullOrWhiteSpace([string]$retired.$property)) {
            throw "Retired compatibility item '$($retired.id)' is missing governance field '$property'."
        }
    }
    foreach ($signal in $retired.signals) {
        $needle = (@($signal.segments) -join '')
        $matches = @($scanFiles | Where-Object {
            (Get-Content $_.FullName -Raw).Contains($needle, [StringComparison]::Ordinal)
        })
        if ($matches.Count -ne 0) {
            $paths = @($matches | ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName) }) -join ', '
            throw "Retired compatibility signal '$needle' remains in active source: $paths"
        }
    }
    $retiredCount++
}

$candidatePattern = '(?i)\b(class|interface|record|struct)\s+\w*(Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite)\w*|\[Obsolete\b|\b(Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite)\w*\s*\(|\$\x22legacy:|SchemaCompatibilityAsync'
$absoluteApiRoutePattern = '\[Http(Get|Post|Put|Delete|Patch)\(\"/api/'
$signals = [System.Collections.Generic.List[object]]::new()
foreach ($file in $scanFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content $file.FullName) {
        $lineNumber++
        if ($line -match $candidatePattern -or $line -match $absoluteApiRoutePattern) {
            $signals.Add([ordered]@{
                path = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
                line = $lineNumber
                text = $line.Trim()
            })
        }
    }
}
$retainedEntries = @($baseline.retainedOrdinaryAbstractions) + @($baseline.retainedCompatibility)
$candidateEntries = @($retainedEntries | Where-Object { $null -ne $_.PSObject.Properties['candidateEvidence'] })
$classifiedSignalCount = 0
$candidateMatchCounts = @{}
foreach ($entry in $candidateEntries) {
    $candidateMatchCounts[[string]$entry.id] = 0
}
foreach ($signal in $signals) {
    $matches = @($candidateEntries | Where-Object {
        [string]$_.candidateEvidence.path -eq [string]$signal.path -and
        ([string]$signal.text).Contains([string]$_.candidateEvidence.pattern, [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) {
        throw "Compatibility signal must have exactly one inventory disposition: $($signal.path):$($signal.line):$($signal.text)"
    }
    $classifiedSignalCount++
    $candidateMatchCounts[[string]$matches[0].id]++
}
foreach ($entry in $candidateEntries) {
    if ([int]$candidateMatchCounts[[string]$entry.id] -eq 0) {
        throw "Compatibility candidate inventory has no current scan evidence: $($entry.id)"
    }
}
$unclassifiedSignals = $signals.Count - $classifiedSignalCount
if ($unclassifiedSignals -ne [int]$baseline.unclassifiedCompatibilitySignals) {
    throw "Unclassified compatibility signal ratchet changed: baseline=$($baseline.unclassifiedCompatibilitySignals) actual=$unclassifiedSignals."
}
if (@($baseline.retainedCompatibility).Count -ne [int]$baseline.activeCompatibilityItems) {
    throw "Active compatibility item count is inconsistent."
}

foreach ($entry in $retainedEntries) {
    foreach ($property in @('disposition', 'decisionReason', 'replacement', 'deletionCondition', 'latestRemovalBatch')) {
        if ($null -eq $entry.PSObject.Properties[$property] -or [string]::IsNullOrWhiteSpace([string]$entry.$property)) {
            throw "Retained inventory item '$($entry.id)' is missing governance field '$property'."
        }
    }
    Assert-FileEvidence $entry.producer "Producer '$($entry.id)'"
    if (@($entry.consumers).Count -eq 0) {
        throw "Retained abstraction has no real consumer evidence: $($entry.id)"
    }
    foreach ($consumer in $entry.consumers) {
        Assert-FileEvidence $consumer "Consumer '$($entry.id)'"
    }
    if ($null -ne $entry.PSObject.Properties['externalConsumers']) {
        if (@($entry.externalConsumers).Count -eq 0 -or
            $null -eq $entry.PSObject.Properties['externalEvidenceHead'] -or
            [string]$entry.externalEvidenceHead -notmatch '^[0-9a-f]{40}$' -or
            $null -eq $entry.PSObject.Properties['externalEvidenceSourceStateSha256'] -or
            [string]$entry.externalEvidenceSourceStateSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "External consumer evidence is incomplete: $($entry.id)"
        }
        if ($edgeHead -ne [string]$entry.externalEvidenceHead) {
            throw "External consumer evidence HEAD changed: baseline=$($entry.externalEvidenceHead) actual=$edgeHead item=$($entry.id)"
        }
        $externalSourceState = [System.Collections.Generic.List[string]]::new()
        foreach ($externalConsumer in $entry.externalConsumers) {
            foreach ($property in @('repository', 'path', 'pattern', 'mustNotContain', 'sourceSha256')) {
                if ($null -eq $externalConsumer.PSObject.Properties[$property] -or
                    [string]::IsNullOrWhiteSpace([string]$externalConsumer.$property)) {
                    throw "External consumer '$($entry.id)' is missing '$property'."
                }
            }
            if ([string]$externalConsumer.repository -ne 'IIoT.EdgeClient' -or
                [string]$externalConsumer.path -notmatch '^src/.+\.cs$' -or
                [string]$externalConsumer.sourceSha256 -notmatch '^[0-9a-f]{64}$') {
                throw "External consumer evidence has an invalid shape: $($entry.id)"
            }
            $externalPath = Join-Path $edgeRoot (([string]$externalConsumer.path) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path $externalPath -PathType Leaf)) {
                throw "External consumer path does not exist: $externalPath"
            }
            $externalSource = Get-Content $externalPath -Raw
            if ((Get-CodePatternCount $externalSource ([string]$externalConsumer.pattern)) -ne 1 -or
                (Get-CodePatternCount $externalSource ([string]$externalConsumer.mustNotContain)) -ne 0) {
                throw "External consumer contract evidence changed: $($externalConsumer.path)"
            }
            $externalHash = (Get-FileHash $externalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($externalHash -ne [string]$externalConsumer.sourceSha256) {
                throw "External consumer source digest changed: $($externalConsumer.path)"
            }
            $externalSourceState.Add("$([string]$externalConsumer.path)`n$externalHash")
        }
        $actualSourceStateSha256 = Get-TextSha256 (@($externalSourceState | Sort-Object) -join "`n")
        if ($actualSourceStateSha256 -ne [string]$entry.externalEvidenceSourceStateSha256) {
            throw "External consumer candidate source-state digest changed: baseline=$($entry.externalEvidenceSourceStateSha256) actual=$actualSourceStateSha256 item=$($entry.id)"
        }
    }
}

$reportPath = Join-Path $reportRoot 'cloud-compatibility.json'
[ordered]@{
    schemaVersion = 2
    ruleId = [string]$baseline.ruleId
    activeCompatibilityItems = [int]$baseline.activeCompatibilityItems
    classifiedCandidateSignals = $classifiedSignalCount
    unclassifiedCompatibilitySignals = $unclassifiedSignals
    retired = $retiredCount
    externalConsumerEvidence = @($baseline.retainedCompatibility | ForEach-Object {
        if ($null -ne $_.PSObject.Properties['externalConsumers']) {
            foreach ($externalConsumer in @($_.externalConsumers)) {
                [ordered]@{
                    compatibilityId = [string]$_.id
                    repository = [string]$externalConsumer.repository
                    path = [string]$externalConsumer.path
                    verified = $true
                    evidenceHead = $edgeHead
                    repositoryClean = $edgeRepositoryClean
                    sourceStateSha256 = [string]$_.externalEvidenceSourceStateSha256
                }
            }
        }
    })
    retained = @($retainedEntries | ForEach-Object {
        [ordered]@{
            id = [string]$_.id
            classification = [string]$_.classification
            producer = [string]$_.producer.path
            consumers = @($_.consumers | ForEach-Object { [string]$_.path })
        }
    })
} | ConvertTo-Json -Depth 20 | Set-Content $reportPath -Encoding utf8

$externalEvidenceCount = @($baseline.retainedCompatibility | ForEach-Object {
    if ($null -ne $_.PSObject.Properties['externalConsumers']) {
        @($_.externalConsumers)
    }
}).Count
if ($externalEvidenceCount -ne [int]$baseline.externalConsumerEvidenceCount) {
    throw "External consumer evidence count changed: baseline=$($baseline.externalConsumerEvidenceCount) actual=$externalEvidenceCount"
}
$externalVerifiedCount = @((Get-Content $reportPath -Raw | ConvertFrom-Json -Depth 100).externalConsumerEvidence |
    Where-Object { $_.verified -eq $true -and $_.repositoryClean -eq $true }).Count
if ($externalVerifiedCount -ne $externalEvidenceCount) {
    throw "External consumer verification did not reconcile: expected=$externalEvidenceCount verified=$externalVerifiedCount"
}
Write-Host "CLOUD_COMPATIBILITY_OK active=$($baseline.activeCompatibilityItems) unclassified=0 classifiedSignals=$classifiedSignalCount retired=$retiredCount retained=$($retainedEntries.Count) externalConsumers=$externalEvidenceCount externalVerified=$externalVerifiedCount output=$reportPath"
