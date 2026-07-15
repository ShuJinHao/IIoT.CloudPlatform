[CmdletBinding()]
param(
    [string]$ReportDirectory = 'artifacts/test-results/mutation',
    [string]$BaseRef = $env:CLOUD_QUALITY_BASE_REF
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'CloudQualityBaselineProtection.ps1')
$baseCommit = Resolve-CloudQualityBaseCommit -RepoRoot $repoRoot -BaseRef $BaseRef
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-mutation.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
$baseBaseline = Get-CloudQualityBaseJson `
    -RepoRoot $repoRoot `
    -BaseCommit $baseCommit `
    -RelativePath 'scripts/tests/baselines/cloud-mutation.json'
if ([int]$baseline.schemaVersion -ne 3 -or [int]$baseBaseline.schemaVersion -lt 1) {
    throw "Cloud mutation baseline schema is invalid: base=$($baseBaseline.schemaVersion) candidate=$($baseline.schemaVersion)."
}
if ([string]$baseline.tool -cne 'dotnet-stryker') {
    throw "Cloud mutation baseline tool must describe the current runner: actual=$($baseline.tool)."
}

function Get-MutationCount($snapshot, [string]$property) {
    if ($null -eq $snapshot.PSObject.Properties[$property]) {
        return 0
    }
    return [int]$snapshot.$property
}

function Get-MutationQualityRates($snapshot, [string]$label) {
    $created = Get-MutationCount $snapshot 'created'
    if ($created -le 0) {
        throw "$label mutation baseline must contain at least one current-source mutant."
    }
    return [pscustomobject]@{
        score = [double]$snapshot.score
        evaluatedRate = [Math]::Round((Get-MutationCount $snapshot 'tested') / $created, 8)
        survivedRate = [Math]::Round((Get-MutationCount $snapshot 'survived') / $created, 8)
        timeoutRate = [Math]::Round((Get-MutationCount $snapshot 'timeout') / $created, 8)
        noCoverageRate = [Math]::Round((Get-MutationCount $snapshot 'noCoverage') / $created, 8)
        runtimeErrorRate = [Math]::Round((Get-MutationCount $snapshot 'runtimeError') / $created, 8)
        compileErrorRate = [Math]::Round((Get-MutationCount $snapshot 'compileError') / $created, 8)
    }
}

function Assert-MutationQualityRatchet($candidate, $base, [string]$label) {
    $candidateRates = Get-MutationQualityRates $candidate "$label candidate"
    $baseRates = Get-MutationQualityRates $base "$label base"
    Assert-CloudQualityAtLeast $candidateRates.score $baseRates.score "$label score"
    Assert-CloudQualityAtLeast $candidateRates.evaluatedRate $baseRates.evaluatedRate "$label evaluated rate"
    foreach ($rate in @('survivedRate', 'timeoutRate', 'noCoverageRate', 'runtimeErrorRate', 'compileErrorRate')) {
        Assert-CloudQualityAtMost ([double]$candidateRates.$rate) ([double]$baseRates.$rate) "$label $rate"
    }
}

Assert-MutationQualityRatchet $baseline $baseBaseline 'mutation'
$identityChangeFixtureBase = [pscustomobject]@{
    score = 100; created = 10; tested = 8; killed = 8; survived = 0
    ignored = 1; compileError = 1; timeout = 0; noCoverage = 0; runtimeError = 0
}
$identityChangeFixtureCandidate = [pscustomobject]@{
    score = 100; created = 20; tested = 16; killed = 16; survived = 0
    ignored = 2; compileError = 2; timeout = 0; noCoverage = 0; runtimeError = 0
}
Assert-MutationQualityRatchet $identityChangeFixtureCandidate $identityChangeFixtureBase 'legal-mutant-count-growth fixture'
$regressionFixture = $null
try {
    $weaker = [pscustomobject]@{
        score = 90; created = 20; tested = 16; killed = 15; survived = 1
        ignored = 2; compileError = 2; timeout = 0; noCoverage = 0; runtimeError = 0
    }
    Assert-MutationQualityRatchet $weaker $identityChangeFixtureBase 'quality-regression fixture'
}
catch {
    $regressionFixture = $_.Exception.Message
}
if ($regressionFixture -notmatch 'weakens') {
    throw "Mutation quality-regression fixture did not fail closed: $regressionFixture"
}
$mutationFingerprints = @($baseline.mutationFingerprints | ForEach-Object { [string]$_ })
if ($mutationFingerprints.Count -ne [int]$baseline.created -or
    @($mutationFingerprints | Where-Object { $_ -notmatch '^[0-9a-f]{64}$' }).Count -ne 0 -or
    @($mutationFingerprints | Sort-Object -Unique).Count -ne $mutationFingerprints.Count) {
    throw 'Cloud mutation baseline must declare one unique stable fingerprint for every current-source mutant.'
}
$targetRelativePath = [string]$baseline.target
$targetPath = Join-Path $repoRoot ($targetRelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path $targetPath -PathType Leaf)) {
    throw "Mutation target is missing: $targetRelativePath"
}
$candidateTargetBlobOutput = @(& git -C $repoRoot hash-object -- $targetPath 2>&1)
$candidateTargetBlob = ($candidateTargetBlobOutput -join '').Trim()
if ($LASTEXITCODE -ne 0 -or
    $candidateTargetBlob -notmatch '^[0-9a-f]{40,64}$' -or
    [string]$baseline.targetSourceBlob -cne $candidateTargetBlob) {
    throw "Mutation baseline does not describe the current target source: actual=$candidateTargetBlob baseline=$($baseline.targetSourceBlob)."
}
$toolManifest = Get-Content (Join-Path $repoRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json -Depth 20
$tool = $toolManifest.tools.'dotnet-stryker'
if ([string]$tool.version -ne [string]$baseline.version -or [bool]$tool.rollForward) {
    throw "Mutation baseline version must match the current dotnet tool manifest and rollForward must remain disabled: baseline=$($baseline.version) manifest=$($tool.version)."
}

$reportRoot = if ([System.IO.Path]::IsPathRooted($ReportDirectory)) {
    $ReportDirectory
} else {
    Join-Path $repoRoot $ReportDirectory
}
Remove-Item $reportRoot -Force -Recurse -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null

$restoreOutput = @(& dotnet tool restore 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "dotnet tool restore failed: $($restoreOutput -join [Environment]::NewLine)"
}

$testProjectPath = Join-Path $repoRoot (([string]$baseline.testProject) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
$testProjectDirectory = Split-Path $testProjectPath -Parent
$outputRelativeToTestProject = [System.IO.Path]::GetRelativePath($testProjectDirectory, $reportRoot)
$targetFileName = [System.IO.Path]::GetFileName([string]$baseline.target)
Push-Location $testProjectDirectory
try {
    $arguments = @(
        'stryker',
        '--test-project', [System.IO.Path]::GetFileName($testProjectPath),
        '--project', 'IIoT.ProductionService.csproj',
        '--mutate', "**/$targetFileName",
        '--reporter', 'Json',
        '--output', $outputRelativeToTestProject,
        '--configuration', 'Release',
        '--skip-version-check',
        '--break-at', '0',
        '--threshold-low', '0',
        '--threshold-high', '100'
    )
    $output = @(& dotnet @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-stryker failed: $($output -join [Environment]::NewLine)"
    }
} finally {
    Pop-Location
}

$reportPath = Join-Path $reportRoot 'reports/mutation-report.json'
if (-not (Test-Path $reportPath -PathType Leaf)) {
    throw "Missing Stryker JSON report: $reportPath"
}
$report = Get-Content $reportPath -Raw | ConvertFrom-Json -Depth 100
$targetEntries = @($report.files.PSObject.Properties | Where-Object {
    ([string]$_.Name).Replace('\', '/').EndsWith("/$targetFileName", [StringComparison]::Ordinal)
})
if ($targetEntries.Count -ne 1) {
    throw "Expected exactly one mutation target report, found $($targetEntries.Count)."
}
$mutants = @($targetEntries[0].Value.mutants)
function Get-StatusCount([string]$status) {
    return @($mutants | Where-Object status -eq $status).Count
}
$killed = Get-StatusCount 'Killed'
$survived = Get-StatusCount 'Survived'
$timeout = Get-StatusCount 'Timeout'
$noCoverage = Get-StatusCount 'NoCoverage'
$ignored = Get-StatusCount 'Ignored'
$compileError = Get-StatusCount 'CompileError'
$runtimeError = Get-StatusCount 'RuntimeError'
$tested = $killed + $survived + $timeout
$score = if ($tested -eq 0) { 0.0 } else { [Math]::Round(100.0 * $killed / $tested, 2) }
function Get-MutationFingerprint($mutant) {
    $identity = "$([string]$mutant.mutatorName)`n$([string]$mutant.replacement)`n$([int]$mutant.location.start.line):$([int]$mutant.location.start.column)-$([int]$mutant.location.end.line):$([int]$mutant.location.end.column)"
    $bytes = [Text.Encoding]::UTF8.GetBytes($identity)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
$actualMutationFingerprints = @($mutants | ForEach-Object { Get-MutationFingerprint $_ } | Sort-Object)
$expectedMutationFingerprints = @($mutationFingerprints | Sort-Object)
$regressionReasons = [System.Collections.Generic.List[string]]::new()
$actualCounts = [ordered]@{
    created = $mutants.Count
    tested = $tested
    killed = $killed
    survived = $survived
    timeout = $timeout
    noCoverage = $noCoverage
    ignored = $ignored
    runtimeError = $runtimeError
    compileError = $compileError
}
foreach ($metric in $actualCounts.Keys) {
    if ([int]$actualCounts[$metric] -ne (Get-MutationCount $baseline $metric)) {
        $regressionReasons.Add("candidate-baseline-$metric=$($actualCounts[$metric])/$((Get-MutationCount $baseline $metric))")
    }
}
if ([Math]::Abs($score - [double]$baseline.score) -gt 0.000000001) {
    $regressionReasons.Add("candidate-baseline-score=$score/$($baseline.score)")
}
if (($actualMutationFingerprints -join "`n") -cne ($expectedMutationFingerprints -join "`n")) {
    $regressionReasons.Add('mutation-fingerprint-set-changed')
}
if (($killed + $survived + $timeout + $noCoverage + $ignored + $runtimeError + $compileError) -ne $mutants.Count) {
    $regressionReasons.Add('unknown-mutation-status')
}
$regressed = $regressionReasons.Count -gt 0
$actualRateSnapshot = [pscustomobject]@{
    created = $mutants.Count
    tested = $tested
    survived = $survived
    timeout = $timeout
    noCoverage = $noCoverage
    runtimeError = $runtimeError
    compileError = $compileError
    score = $score
}
$actualRates = Get-MutationQualityRates $actualRateSnapshot 'actual report'

$summaryPath = Join-Path $reportRoot 'cloud-mutation-summary.json'
[ordered]@{
    schemaVersion = 1
    tool = [string]$baseline.tool
    version = [string]$baseline.version
    target = [string]$baseline.target
    created = $mutants.Count
    tested = $tested
    killed = $killed
    survived = $survived
    timeout = $timeout
    noCoverage = $noCoverage
    ignored = $ignored
    runtimeError = $runtimeError
    compileError = $compileError
    score = $score
    rates = $actualRates
    baselineScore = [double]$baseline.score
    regressed = $regressed
    regressionReasons = $regressionReasons
    baseCommit = $baseCommit
    targetSourceBlob = $candidateTargetBlob
    mutationFingerprintCount = $actualMutationFingerprints.Count
    gate = [string]$baseline.gate
} | ConvertTo-Json -Depth 10 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_MUTATION_REPORT_OK target=$targetFileName created=$($mutants.Count) tested=$tested killed=$killed survived=$survived timeout=$timeout noCoverage=$noCoverage ignored=$ignored runtimeError=$runtimeError compileError=$compileError score=$score evaluatedRate=$($actualRates.evaluatedRate) regressed=$regressed output=$summaryPath"
if ($regressed) {
    throw "Cloud mutation ratchet failed: $($regressionReasons -join ', ')."
}
