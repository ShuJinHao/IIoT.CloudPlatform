[CmdletBinding()]
param(
    [string]$ReportDirectory = 'artifacts/test-results/mutation'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-mutation.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
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
$regressed = $score -lt [double]$baseline.score -or $survived -gt [int]$baseline.survived

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
    gate = [string]$baseline.gate
} | ConvertTo-Json -Depth 10 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_MUTATION_REPORT_OK target=$targetFileName created=$($mutants.Count) tested=$tested killed=$killed survived=$survived timeout=$timeout noCoverage=$noCoverage ignored=$ignored compileError=$compileError score=$score regressed=$regressed output=$summaryPath"
