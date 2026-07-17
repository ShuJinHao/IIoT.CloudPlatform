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
if ([int]$baseline.schemaVersion -ne 4 -or [int]$baseline.externalConsumerEvidenceCount -lt 0) {
    throw 'Cloud compatibility baseline must use schemaVersion=4 and declare a non-negative current externalConsumerEvidenceCount.'
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

$candidateRetainedIds = @($baseline.retainedCompatibility | ForEach-Object { [string]$_.id })
$candidateRetiredIds = @($baseline.retired | ForEach-Object { [string]$_.id })
if (@($candidateRetainedIds | Sort-Object -Unique).Count -ne $candidateRetainedIds.Count -or
    @($candidateRetiredIds | Sort-Object -Unique).Count -ne $candidateRetiredIds.Count -or
    @($candidateRetainedIds | Where-Object { $_ -in $candidateRetiredIds }).Count -ne 0) {
    throw 'Cloud compatibility inventory contains duplicate or conflicting active/retired ids.'
}
foreach ($baseEntry in @($baseBaseline.retainedCompatibility)) {
    $id = [string]$baseEntry.id
    $candidateEntry = Get-InventoryEntryById $baseline.retainedCompatibility $id 'candidate retainedCompatibility'
    if ($null -eq $candidateEntry) {
        if ($id -notin $candidateRetiredIds) {
            throw "Existing compatibility item '$id' disappeared without a physical-retirement inventory record."
        }
        continue
    }
    $baseExternalCount = if ($null -eq $baseEntry.PSObject.Properties['externalConsumers']) {
        0
    } else {
        @($baseEntry.externalConsumers).Count
    }
    $candidateExternalCount = if ($null -eq $candidateEntry.PSObject.Properties['externalConsumers']) {
        0
    } else {
        @($candidateEntry.externalConsumers).Count
    }
    Assert-CloudQualityAtLeast $candidateExternalCount $baseExternalCount "retained compatibility '$id' external evidence"
}
$newRetainedIds = @($candidateRetainedIds | Where-Object {
    $id = $_
    $null -eq (Get-InventoryEntryById $baseBaseline.retainedCompatibility $id 'base retainedCompatibility')
})
if ($newRetainedIds.Count -gt 0) {
    throw "Active compatibility items may not grow; physically remove the new compatibility path instead: $($newRetainedIds -join ', ')."
}
foreach ($baseRetired in @($baseBaseline.retired)) {
    if ([string]$baseRetired.id -notin $candidateRetiredIds) {
        throw "Retired compatibility item was resurrected or removed from inventory: $($baseRetired.id)"
    }
}
$candidateOrdinaryIds = @($baseline.retainedOrdinaryAbstractions | ForEach-Object { [string]$_.id })
if (@($candidateOrdinaryIds | Sort-Object -Unique).Count -ne $candidateOrdinaryIds.Count) {
    throw 'Candidate ordinary abstraction inventory contains duplicate ids.'
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

function Assert-OrdinaryEvidenceShape($entry, [string]$label) {
    foreach ($property in @('id', 'classification', 'disposition', 'decisionReason', 'producer', 'candidateEvidence', 'consumers', 'tests')) {
        if ($null -eq $entry.PSObject.Properties[$property]) {
            throw "$label is missing current machine-verifiable '$property'."
        }
    }
    if ([string]$entry.classification -cne 'current-ordinary-abstraction' -or
        [string]$entry.disposition -cne 'notCompatibility' -or
        [string]$entry.producer.path -cne [string]$entry.candidateEvidence.path -or
        [string]$entry.producer.pattern -cne [string]$entry.candidateEvidence.pattern -or
        [string]$entry.producer.path -notmatch '^src/(core|hosts|infrastructure|services|shared|ui/iiot-web/src)/' -or
        [string]$entry.producer.path -match '(?i)(Tests?|Testing|TestKit|Fakes?|Mocks?)/') {
        throw "$label is not a uniquely classified production abstraction."
    }
    if (@($entry.consumers).Count -eq 0 -or @($entry.tests).Count -eq 0) {
        throw "$label requires real executable production callers and tests."
    }
    foreach ($consumer in @($entry.consumers)) {
        if ([string]$consumer.path -ceq [string]$entry.producer.path -or
            [string]$consumer.path -notmatch '^src/(core|hosts|infrastructure|services|shared|ui/iiot-web/src)/') {
            throw "$label has no independent production caller."
        }
    }
    foreach ($testEvidence in @($entry.tests)) {
        if ([string]$testEvidence.path -notmatch '^src/(tests|ui/iiot-web/src)/' -or
            ([string]$testEvidence.path -match '^src/ui/' -and
             [string]$testEvidence.path -notmatch '\.(test|spec)\.(ts|js)$')) {
            throw "$label has invalid test evidence."
        }
    }
}

function Assert-OrdinaryProducerSemantics(
    [string]$source,
    [string]$pattern,
    [string]$label,
    [switch]$Configuration) {
    if ((-not $Configuration -and (Get-CodePatternCount $source $pattern) -ne 1) -or
        $source -match '(?i)\[(?:System\.)?Obsolete\b|\blegacy:|dual.?write|shadow.?path|old.?endpoint') {
        throw "$label has ambiguous or compatibility-bearing producer semantics."
    }
}

$legalAdapterFixture = @'
{"id":"adapter","classification":"current-ordinary-abstraction","disposition":"notCompatibility","decisionReason":"current port","producer":{"path":"src/services/CurrentAdapter.cs","pattern":"class CurrentAdapter"},"candidateEvidence":{"path":"src/services/CurrentAdapter.cs","pattern":"class CurrentAdapter"},"consumers":[{"path":"src/hosts/AdapterConsumer.cs","pattern":"CurrentAdapter"}],"tests":[{"path":"src/tests/AdapterTests.cs","pattern":"CurrentAdapter"}]}
'@ | ConvertFrom-Json -Depth 10
Assert-OrdinaryEvidenceShape $legalAdapterFixture 'legal Adapter fixture'
Assert-OrdinaryProducerSemantics 'internal sealed class CurrentAdapter { }' 'class CurrentAdapter' 'legal Adapter fixture'
$legalMovedWrapperFixture = @'
{"id":"wrapper-moved","classification":"current-ordinary-abstraction","disposition":"notCompatibility","decisionReason":"current port moved with callers","producer":{"path":"src/infrastructure/New/CurrentWrapper.cs","pattern":"class CurrentWrapper"},"candidateEvidence":{"path":"src/infrastructure/New/CurrentWrapper.cs","pattern":"class CurrentWrapper"},"consumers":[{"path":"src/services/WrapperConsumer.cs","pattern":"CurrentWrapper"}],"tests":[{"path":"src/tests/WrapperTests.cs","pattern":"CurrentWrapper"}]}
'@ | ConvertFrom-Json -Depth 10
Assert-OrdinaryEvidenceShape $legalMovedWrapperFixture 'legal moved Wrapper fixture'

foreach ($invalidFixture in @(
    [pscustomobject]@{ label = 'zero-caller fixture'; json = '{"id":"zero","classification":"current-ordinary-abstraction","disposition":"notCompatibility","decisionReason":"invalid","producer":{"path":"src/services/Zero.cs","pattern":"class Zero"},"candidateEvidence":{"path":"src/services/Zero.cs","pattern":"class Zero"},"consumers":[],"tests":[{"path":"src/tests/ZeroTests.cs","pattern":"Zero"}]}' },
    [pscustomobject]@{ label = 'test-only caller fixture'; json = '{"id":"test-only","classification":"current-ordinary-abstraction","disposition":"notCompatibility","decisionReason":"invalid","producer":{"path":"src/services/TestOnly.cs","pattern":"class TestOnly"},"candidateEvidence":{"path":"src/services/TestOnly.cs","pattern":"class TestOnly"},"consumers":[{"path":"src/tests/TestOnlyTests.cs","pattern":"TestOnly"}],"tests":[{"path":"src/tests/TestOnlyTests.cs","pattern":"TestOnly"}]}' },
    [pscustomobject]@{ label = 'self-call fixture'; json = '{"id":"self","classification":"current-ordinary-abstraction","disposition":"notCompatibility","decisionReason":"invalid","producer":{"path":"src/services/Self.cs","pattern":"class Self"},"candidateEvidence":{"path":"src/services/Self.cs","pattern":"class Self"},"consumers":[{"path":"src/services/Self.cs","pattern":"Self"}],"tests":[{"path":"src/tests/SelfTests.cs","pattern":"Self"}]}' }
)) {
    $failure = $null
    try {
        Assert-OrdinaryEvidenceShape ($invalidFixture.json | ConvertFrom-Json -Depth 10) $invalidFixture.label
    }
    catch {
        $failure = $_.Exception.Message
    }
    if ([string]::IsNullOrWhiteSpace($failure)) {
        throw "$($invalidFixture.label) did not fail closed."
    }
}
$compatibilitySemanticsFixture = $null
try {
    Assert-OrdinaryProducerSemantics 'internal sealed class DualWriteAdapter { }' 'class DualWriteAdapter' 'compatibility-semantics fixture'
}
catch {
    $compatibilitySemanticsFixture = $_.Exception.Message
}
if ($compatibilitySemanticsFixture -notmatch 'compatibility-bearing') {
    throw "Compatibility-semantics fixture did not fail closed: $compatibilitySemanticsFixture"
}

foreach ($entry in @($baseline.retainedOrdinaryAbstractions)) {
    $ordinaryId = [string]$entry.id
    Assert-OrdinaryEvidenceShape $entry "Ordinary abstraction '$ordinaryId'"
    $producerPath = Resolve-RepoPath ([string]$entry.producer.path)
    if (-not (Test-Path $producerPath -PathType Leaf)) {
        throw "Ordinary abstraction '$ordinaryId' producer does not exist: $($entry.producer.path)"
    }
    $producerSource = Get-Content $producerPath -Raw
    $producerMatchCount = if ([System.IO.Path]::GetExtension($producerPath) -in @('.json', '.yml', '.yaml', '.conf')) {
        [regex]::Matches($producerSource, [regex]::Escape([string]$entry.producer.pattern)).Count
    } else {
        Get-CodePatternCount $producerSource ([string]$entry.producer.pattern)
    }
    if ($producerMatchCount -ne 1) {
        throw "Ordinary abstraction '$ordinaryId' has ambiguous producer evidence."
    }
    $isProducerConfiguration = [System.IO.Path]::GetExtension($producerPath) -in @('.json', '.yml', '.yaml', '.conf')
    Assert-OrdinaryProducerSemantics `
        $producerSource `
        ([string]$entry.producer.pattern) `
        "Ordinary abstraction '$ordinaryId'" `
        -Configuration:$isProducerConfiguration
    foreach ($consumer in @($entry.consumers)) {
        Assert-FileEvidence $consumer "Ordinary consumer '$ordinaryId'"
    }
    foreach ($testEvidence in @($entry.tests)) {
        Assert-FileEvidence $testEvidence "Ordinary test '$ordinaryId'"
    }
}

function Get-TextSha256([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-ExternalBindingState(
    [string]$expectedTrackedSourceStateSha256,
    [string]$actualTrackedSourceStateSha256,
    [string]$expectedConsumerStateSha256,
    [string]$actualConsumerStateSha256,
    [bool]$repositoryClean,
    [string]$referenceHead,
    [string]$actualHead,
    [string]$label) {
    if (-not $repositoryClean) {
        throw "$label repository must be clean."
    }
    if ($actualTrackedSourceStateSha256 -cne $expectedTrackedSourceStateSha256) {
        throw "$label tracked src source-state digest changed: baseline=$expectedTrackedSourceStateSha256 actual=$actualTrackedSourceStateSha256"
    }
    if ($actualConsumerStateSha256 -cne $expectedConsumerStateSha256) {
        throw "$label declared consumer aggregate digest changed: baseline=$expectedConsumerStateSha256 actual=$actualConsumerStateSha256"
    }
    return [ordered]@{
        referenceHead = $referenceHead
        actualHead = $actualHead
        headMatchesReference = $actualHead -ceq $referenceHead
    }
}

function Assert-ExternalConsumerContract(
    $evidence,
    [string]$source,
    [string]$actualSourceSha256,
    [string]$label) {
    foreach ($property in @(
        'repository',
        'path',
        'pattern',
        'expectedPatternCount',
        'mustNotContain',
        'expectedMustNotContainCount',
        'sourceSha256')) {
        if ($null -eq $evidence.PSObject.Properties[$property] -or
            [string]::IsNullOrWhiteSpace([string]$evidence.$property)) {
            throw "$label is missing '$property'."
        }
    }
    if ([string]$evidence.repository -cne 'IIoT.EdgeClient' -or
        [string]$evidence.path -notmatch '^src/.+\.cs$' -or
        [string]$evidence.sourceSha256 -notmatch '^[0-9a-f]{64}$' -or
        [int]$evidence.expectedPatternCount -lt 0 -or
        [int]$evidence.expectedMustNotContainCount -lt 0) {
        throw "$label has an invalid shape."
    }
    if ((Get-CodePatternCount $source ([string]$evidence.pattern)) -ne
            [int]$evidence.expectedPatternCount -or
        (Get-CodePatternCount $source ([string]$evidence.mustNotContain)) -ne
            [int]$evidence.expectedMustNotContainCount) {
        throw "$label executable contract evidence changed."
    }
    if ($actualSourceSha256 -cne [string]$evidence.sourceSha256) {
        throw "$label source digest changed."
    }
}

$fixtureSourceHash = 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'
$validExternalConsumerFixture = [pscustomobject]@{
    repository = 'IIoT.EdgeClient'
    path = 'src/Fixture.cs'
    pattern = 'Execute()'
    expectedPatternCount = 1
    mustNotContain = 'RequestId'
    expectedMustNotContainCount = 0
    sourceSha256 = $fixtureSourceHash
}
Assert-ExternalConsumerContract `
    $validExternalConsumerFixture 'await Execute();' $fixtureSourceHash 'valid external consumer fixture'
$externalConsumerNegativeFixtures = @(
    [pscustomobject]@{ label = 'external consumer path fixture'; property = 'path'; value = 'docs/Fixture.cs' },
    [pscustomobject]@{ label = 'external consumer SHA fixture'; property = 'sourceSha256'; value = ('e' * 64) },
    [pscustomobject]@{ label = 'external consumer pattern fixture'; property = 'pattern'; value = 'Missing()' },
    [pscustomobject]@{ label = 'external consumer must-not-contain fixture'; property = 'mustNotContain'; value = 'Execute()' }
)
foreach ($fixture in $externalConsumerNegativeFixtures) {
    $invalidEvidence = ($validExternalConsumerFixture | ConvertTo-Json -Depth 10 | ConvertFrom-Json -Depth 10)
    $invalidEvidence.([string]$fixture.property) = [string]$fixture.value
    $failure = $null
    try {
        Assert-ExternalConsumerContract `
            $invalidEvidence 'await Execute();' $fixtureSourceHash ([string]$fixture.label)
    }
    catch {
        $failure = $_.Exception.Message
    }
    if ([string]::IsNullOrWhiteSpace($failure)) {
        throw "$($fixture.label) did not fail closed."
    }
}

$fixtureDigest = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$fixtureReferenceHead = '1111111111111111111111111111111111111111'
$fixtureDocOnlyHead = '2222222222222222222222222222222222222222'
$docOnlyFixture = Assert-ExternalBindingState `
    $fixtureDigest $fixtureDigest $fixtureDigest $fixtureDigest $true `
    $fixtureReferenceHead $fixtureDocOnlyHead 'doc-only HEAD fixture'
if ($docOnlyFixture.headMatchesReference) {
    throw 'Doc-only HEAD fixture failed to report a non-matching informational reference HEAD.'
}
$externalBindingNegativeFixtures = @(
    [pscustomobject]@{ label = 'tracked src drift fixture'; source = ('b' * 64); consumer = $fixtureDigest; clean = $true },
    [pscustomobject]@{ label = 'consumer aggregate drift fixture'; source = $fixtureDigest; consumer = ('c' * 64); clean = $true },
    [pscustomobject]@{ label = 'dirty repository fixture'; source = $fixtureDigest; consumer = $fixtureDigest; clean = $false }
)
foreach ($fixture in $externalBindingNegativeFixtures) {
    $failure = $null
    try {
        $null = Assert-ExternalBindingState `
            $fixtureDigest ([string]$fixture.source) `
            $fixtureDigest ([string]$fixture.consumer) `
            ([bool]$fixture.clean) $fixtureReferenceHead $fixtureReferenceHead ([string]$fixture.label)
    }
    catch {
        $failure = $_.Exception.Message
    }
    if ([string]::IsNullOrWhiteSpace($failure)) {
        throw "$($fixture.label) did not fail closed."
    }
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
$edgeTrackedSourceStateLines = @(& git -C $edgeRoot ls-files --stage -- src 2>&1)
if ($LASTEXITCODE -ne 0 -or $edgeTrackedSourceStateLines.Count -eq 0) {
    throw "Unable to resolve the complete tracked Edge src source-state: $($edgeTrackedSourceStateLines -join ' ')"
}
$edgeTrackedSourceStateSha256 = Get-TextSha256 ($edgeTrackedSourceStateLines -join "`n")

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
    foreach ($property in @('producer', 'consumerEvidence', 'replacement', 'deletionCondition')) {
        if ($null -eq $retired.PSObject.Properties[$property] -or [string]::IsNullOrWhiteSpace([string]$retired.$property)) {
            throw "Retired compatibility item '$($retired.id)' is missing physical-removal evidence field '$property'."
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
    if ([int]$candidateMatchCounts[[string]$entry.id] -eq 0 -and
        [string]$entry.disposition -ne 'notCompatibility') {
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
    $requiredContractFields = @('disposition', 'decisionReason')
    if ([string]$entry.disposition -ne 'notCompatibility') {
        $requiredContractFields += @('replacement', 'deletionCondition')
    }
    foreach ($property in $requiredContractFields) {
        if ($null -eq $entry.PSObject.Properties[$property] -or [string]::IsNullOrWhiteSpace([string]$entry.$property)) {
            throw "Retained inventory item '$($entry.id)' is missing contract evidence field '$property'."
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
            [string]$entry.externalEvidenceBinding -cne 'clean-tracked-src-v1' -or
            $null -eq $entry.PSObject.Properties['externalEvidenceReferenceHead'] -or
            [string]$entry.externalEvidenceReferenceHead -notmatch '^[0-9a-f]{40}$' -or
            $null -eq $entry.PSObject.Properties['externalEvidenceTrackedSourceStateSha256'] -or
            [string]$entry.externalEvidenceTrackedSourceStateSha256 -notmatch '^[0-9a-f]{64}$' -or
            $null -eq $entry.PSObject.Properties['externalEvidenceConsumerStateSha256'] -or
            [string]$entry.externalEvidenceConsumerStateSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "External consumer evidence is incomplete: $($entry.id)"
        }
        $externalSourceState = [System.Collections.Generic.List[string]]::new()
        foreach ($externalConsumer in $entry.externalConsumers) {
            $externalPath = Join-Path $edgeRoot (([string]$externalConsumer.path) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path $externalPath -PathType Leaf)) {
                throw "External consumer path does not exist: $externalPath"
            }
            $externalSource = Get-Content $externalPath -Raw
            $externalHash = (Get-FileHash $externalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            Assert-ExternalConsumerContract `
                $externalConsumer `
                $externalSource `
                $externalHash `
                "External consumer '$($entry.id)/$($externalConsumer.path)'"
            $externalSourceState.Add("$([string]$externalConsumer.path)`n$externalHash")
        }
        $actualSourceStateSha256 = Get-TextSha256 (@($externalSourceState | Sort-Object) -join "`n")
        $entry | Add-Member -NotePropertyName externalEvidenceActualState -NotePropertyValue (
            Assert-ExternalBindingState `
                ([string]$entry.externalEvidenceTrackedSourceStateSha256) `
                $edgeTrackedSourceStateSha256 `
                ([string]$entry.externalEvidenceConsumerStateSha256) `
                $actualSourceStateSha256 `
                $edgeRepositoryClean `
                ([string]$entry.externalEvidenceReferenceHead) `
                $edgeHead `
                "External consumer '$($entry.id)'"
        ) -Force
    }
}

$reportPath = Join-Path $reportRoot 'cloud-compatibility.json'
[ordered]@{
    schemaVersion = 3
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
                    referenceHead = [string]$_.externalEvidenceReferenceHead
                    evidenceHead = [string]$_.externalEvidenceActualState.actualHead
                    headMatchesReference = [bool]$_.externalEvidenceActualState.headMatchesReference
                    repositoryClean = $edgeRepositoryClean
                    trackedSourceStateSha256 = $edgeTrackedSourceStateSha256
                    consumerStateSha256 = [string]$_.externalEvidenceConsumerStateSha256
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
