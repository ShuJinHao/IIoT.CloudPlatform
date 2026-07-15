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
if ([int]$baseline.schemaVersion -ne 2 -or [int]$baseBaseline.schemaVersion -lt 1) {
    throw "Cloud mutation baseline schema is invalid: base=$($baseBaseline.schemaVersion) candidate=$($baseline.schemaVersion)."
}
foreach ($property in @('tool', 'version', 'target', 'testProject')) {
    if ([string]$baseline.$property -cne [string]$baseBaseline.$property) {
        throw "Cloud mutation baseline rewrites immutable '$property': base='$($baseBaseline.$property)' candidate='$($baseline.$property)'."
    }
}
Assert-CloudQualityAtLeast ([double]$baseline.score) ([double]$baseBaseline.score) 'mutation score threshold'
Assert-CloudQualityAtLeast ([int]$baseline.tested) ([int]$baseBaseline.tested) 'mutation tested count'
Assert-CloudQualityAtLeast ([int]$baseline.killed) ([int]$baseBaseline.killed) 'mutation killed count'
Assert-CloudQualityAtMost ([int]$baseline.survived) ([int]$baseBaseline.survived) 'mutation survived ceiling'
Assert-CloudQualityAtMost ([int]$baseline.ignored) ([int]$baseBaseline.ignored) 'mutation ignored ceiling'
Assert-CloudQualityAtMost ([int]$baseline.compileError) ([int]$baseBaseline.compileError) 'mutation compile-error ceiling'
if ([int]$baseline.created -ne [int]$baseBaseline.created) {
    throw "Mutation id-set cardinality changed relative to immutable base: base=$($baseBaseline.created) candidate=$($baseline.created)."
}
$baseTimeout = if ($null -ne $baseBaseline.PSObject.Properties['timeout']) { [int]$baseBaseline.timeout } else { 0 }
$baseNoCoverage = if ($null -ne $baseBaseline.PSObject.Properties['noCoverage']) { [int]$baseBaseline.noCoverage } else { 0 }
Assert-CloudQualityAtMost ([int]$baseline.timeout) $baseTimeout 'mutation timeout ceiling'
Assert-CloudQualityAtMost ([int]$baseline.noCoverage) $baseNoCoverage 'mutation no-coverage ceiling'
$mutationFingerprints = @($baseline.mutationFingerprints | ForEach-Object { [string]$_ })
if ($mutationFingerprints.Count -ne [int]$baseline.created -or
    @($mutationFingerprints | Where-Object { $_ -notmatch '^[0-9a-f]{64}$' }).Count -ne 0 -or
    @($mutationFingerprints | Sort-Object -Unique).Count -ne $mutationFingerprints.Count) {
    throw 'Cloud mutation baseline must declare one unique stable fingerprint for every immutable-base mutant.'
}
$targetRelativePath = [string]$baseline.target
$baseTargetBlobOutput = @(& git -C $repoRoot rev-parse "$baseCommit`:$targetRelativePath" 2>&1)
$baseTargetBlob = ($baseTargetBlobOutput -join '').Trim()
if ($LASTEXITCODE -ne 0 -or $baseTargetBlob -notmatch '^[0-9a-f]{40,64}$') {
    throw "Unable to resolve immutable-base mutation target blob: $($baseTargetBlobOutput -join ' ')"
}
$targetPath = Join-Path $repoRoot ($targetRelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path $targetPath -PathType Leaf)) {
    throw "Mutation target is missing: $targetRelativePath"
}
$candidateTargetBlobOutput = @(& git -C $repoRoot hash-object -- $targetPath 2>&1)
$candidateTargetBlob = ($candidateTargetBlobOutput -join '').Trim()
if ($LASTEXITCODE -ne 0 -or
    $candidateTargetBlob -cne $baseTargetBlob -or
    [string]$baseline.targetSourceBlob -cne $baseTargetBlob) {
    throw "Mutation target/source baseline changed relative to immutable base: base=$baseTargetBlob candidate=$candidateTargetBlob baseline=$($baseline.targetSourceBlob)."
}
$toolManifest = Get-Content (Join-Path $repoRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json -Depth 20
$tool = $toolManifest.tools.'dotnet-stryker'
if ([string]$tool.version -ne [string]$baseline.version -or [bool]$tool.rollForward) {
    throw "dotnet-stryker must be pinned to $($baseline.version) with rollForward disabled."
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
$tested = $killed + $survived + $timeout + $noCoverage
$score = if ($tested -eq 0) { 0.0 } else { [Math]::Round(100.0 * $killed / $tested, 2) }
function Get-MutationFingerprint($mutant) {
    $identity = "$([string]$mutant.mutatorName)`n$([string]$mutant.replacement)`n$([int]$mutant.location.start.line):$([int]$mutant.location.start.column)-$([int]$mutant.location.end.line):$([int]$mutant.location.end.column)"
    $bytes = [Text.Encoding]::UTF8.GetBytes($identity)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
$actualMutationFingerprints = @($mutants | ForEach-Object { Get-MutationFingerprint $_ } | Sort-Object)
$expectedMutationFingerprints = @($mutationFingerprints | Sort-Object)
$regressionReasons = [System.Collections.Generic.List[string]]::new()
if ($mutants.Count -ne [int]$baseline.created) { $regressionReasons.Add("created=$($mutants.Count)/$($baseline.created)") }
if ($tested -lt [int]$baseline.tested) { $regressionReasons.Add("tested=$tested/$($baseline.tested)") }
if ($killed -lt [int]$baseline.killed) { $regressionReasons.Add("killed=$killed/$($baseline.killed)") }
if ($survived -gt [int]$baseline.survived) { $regressionReasons.Add("survived=$survived/$($baseline.survived)") }
if ($timeout -gt [int]$baseline.timeout) { $regressionReasons.Add("timeout=$timeout/$($baseline.timeout)") }
if ($noCoverage -gt [int]$baseline.noCoverage) { $regressionReasons.Add("noCoverage=$noCoverage/$($baseline.noCoverage)") }
if ($ignored -gt [int]$baseline.ignored) { $regressionReasons.Add("ignored=$ignored/$($baseline.ignored)") }
if ($compileError -gt [int]$baseline.compileError) { $regressionReasons.Add("compileError=$compileError/$($baseline.compileError)") }
if ($score -lt [double]$baseline.score) { $regressionReasons.Add("score=$score/$($baseline.score)") }
if (($actualMutationFingerprints -join "`n") -cne ($expectedMutationFingerprints -join "`n")) {
    $regressionReasons.Add('mutation-fingerprint-set-changed')
}
if (($killed + $survived + $timeout + $noCoverage + $ignored + $compileError) -ne $mutants.Count) {
    $regressionReasons.Add('unknown-mutation-status')
}
$regressed = $regressionReasons.Count -gt 0

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
    compileError = $compileError
    score = $score
    baselineScore = [double]$baseline.score
    regressed = $regressed
    regressionReasons = $regressionReasons
    immutableBaseCommit = $baseCommit
    targetSourceBlob = $candidateTargetBlob
    mutationFingerprintCount = $actualMutationFingerprints.Count
    gate = [string]$baseline.gate
} | ConvertTo-Json -Depth 10 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_MUTATION_REPORT_OK target=$targetFileName created=$($mutants.Count) tested=$tested killed=$killed survived=$survived timeout=$timeout noCoverage=$noCoverage ignored=$ignored compileError=$compileError score=$score regressed=$regressed output=$summaryPath"
if ($regressed) {
    throw "Cloud mutation ratchet failed: $($regressionReasons -join ', ')."
}
