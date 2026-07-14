[CmdletBinding()]
param(
    [ValidateSet('Required', 'EndToEnd')]
    [string]$Mode = 'Required',
    [string]$VitestResult = 'artifacts/test-results/vitest.json',
    [string]$BrowserSmokeResult = 'artifacts/test-results/playwright-browser-smoke.json',
    [string]$EndToEndResult = 'artifacts/test-results/playwright-real-e2e.json',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$webRoot = Join-Path $repoRoot 'src/ui/iiot-web'
$manifest = Get-Content (Join-Path $webRoot 'test-inventory.json') -Raw | ConvertFrom-Json -Depth 100

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $repoRoot ($path -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function Normalize-Path([string]$path) {
    return [System.IO.Path]::GetFullPath($path).Replace('\', '/')
}

function Assert-ManifestTotal($section, [string]$name) {
    $entries = @($section.files)
    if ($entries.Count -ne [int]$section.expectedFiles -or
        [int](($entries | Measure-Object -Property expected -Sum).Sum) -ne [int]$section.expectedCases) {
        throw "Cloud Web $name inventory totals are inconsistent."
    }
}

Assert-ManifestTotal $manifest.vitest 'Vitest'
Assert-ManifestTotal $manifest.browserSmoke 'Browser Smoke'
Assert-ManifestTotal $manifest.endToEnd 'EndToEnd'

$vitestEntries = @($manifest.vitest.files)
$allowedVitestKinds = @('Unit', 'Component', 'Contract', 'Smoke')
foreach ($kind in $allowedVitestKinds) {
    if (@($vitestEntries | Where-Object testKind -eq $kind).Count -eq 0) {
        throw "Cloud Web inventory has no real Vitest file classified as $kind."
    }
}
if (@($vitestEntries | Where-Object { $_.testKind -notin $allowedVitestKinds }).Count -ne 0 -or
    @($manifest.browserSmoke.files | Where-Object testKind -ne 'BrowserSmoke').Count -ne 0 -or
    @($manifest.endToEnd.files | Where-Object testKind -ne 'EndToEnd').Count -ne 0) {
    throw 'Cloud Web inventory contains an unsupported or ambiguous TestKind.'
}

function Assert-FileInventory([string]$directory, [string]$filter, $entries, [string]$name) {
    $actual = @(Get-ChildItem (Join-Path $webRoot $directory) -File -Recurse -Filter $filter |
        ForEach-Object { [System.IO.Path]::GetRelativePath($webRoot, $_.FullName).Replace('\', '/') } |
        Sort-Object)
    $expected = @($entries | ForEach-Object { [string]$_.path } | Sort-Object)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "$name file inventory mismatch.`nManifest:`n$($expected -join "`n")`nActual:`n$($actual -join "`n")"
    }
}

$actualVitestFiles = @(Get-ChildItem (Join-Path $webRoot 'src') -File -Recurse |
    Where-Object { $_.Name -match '\.(test|spec)\.ts$' } |
    ForEach-Object { [System.IO.Path]::GetRelativePath($webRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$manifestVitestFiles = @($vitestEntries | ForEach-Object { [string]$_.path } | Sort-Object)
if (($actualVitestFiles -join "`n") -ne ($manifestVitestFiles -join "`n")) {
    throw 'Vitest file inventory mismatch.'
}
Assert-FileInventory 'browser-smoke' '*.spec.ts' $manifest.browserSmoke.files 'Browser Smoke'
Assert-FileInventory 'e2e' '*.spec.ts' $manifest.endToEnd.files 'EndToEnd'

function Get-PlaywrightSpecs($suite) {
    $collected = [System.Collections.Generic.List[object]]::new()
    if ($null -ne $suite.PSObject.Properties['specs']) {
        foreach ($spec in @($suite.specs)) { $collected.Add($spec) }
    }
    if ($null -ne $suite.PSObject.Properties['suites']) {
        foreach ($child in @($suite.suites)) {
            foreach ($spec in Get-PlaywrightSpecs $child) { $collected.Add($spec) }
        }
    }
    return $collected
}

function Add-PlaywrightEvidence(
    [string]$resultPath,
    $section,
    [string]$runner,
    [System.Collections.Generic.List[object]]$evidence) {
    $playwright = Get-Content (Resolve-RepoPath $resultPath) -Raw | ConvertFrom-Json -Depth 100
    if ([int]$playwright.stats.expected -ne [int]$section.expectedCases -or
        [int]$playwright.stats.unexpected -ne 0 -or
        [int]$playwright.stats.flaky -ne 0 -or
        [int]$playwright.stats.skipped -ne 0) {
        throw "$runner discovery/execution reconciliation failed."
    }
    $allSpecs = [System.Collections.Generic.List[object]]::new()
    foreach ($suite in @($playwright.suites)) {
        foreach ($spec in Get-PlaywrightSpecs $suite) { $allSpecs.Add($spec) }
    }
    foreach ($entry in @($section.files)) {
        $matches = @($allSpecs | Where-Object {
            ([string]$_.file).Replace('\', '/').EndsWith([string]$entry.path, [StringComparison]::Ordinal)
        })
        if ($matches.Count -ne [int]$entry.expected -or
            @($matches | Where-Object { -not [bool]$_.ok }).Count -ne 0) {
            throw "$runner case reconciliation failed for $($entry.path)."
        }
        foreach ($spec in $matches) {
            $evidence.Add([ordered]@{
                runner = $runner
                file = [string]$entry.path
                testKind = [string]$entry.testKind
                case = [string]$spec.title
                status = 'Passed'
            })
        }
    }
}

$caseEvidence = [System.Collections.Generic.List[object]]::new()
if ($Mode -eq 'Required') {
    $vitest = Get-Content (Resolve-RepoPath $VitestResult) -Raw | ConvertFrom-Json -Depth 100
    if (-not [bool]$vitest.success -or
        [int]$vitest.numTotalTests -ne [int]$manifest.vitest.expectedCases -or
        [int]$vitest.numPassedTests -ne [int]$manifest.vitest.expectedCases -or
        [int]$vitest.numFailedTests -ne 0 -or
        [int]$vitest.numPendingTests -ne 0 -or
        @($vitest.testResults).Count -ne [int]$manifest.vitest.expectedFiles) {
        throw 'Vitest discovery/execution reconciliation failed.'
    }
    foreach ($entry in $vitestEntries) {
        $expectedPath = Normalize-Path (Join-Path $webRoot ([string]$entry.path))
        $matches = @($vitest.testResults | Where-Object { (Normalize-Path ([string]$_.name)) -eq $expectedPath })
        if ($matches.Count -ne 1) {
            throw "Expected one Vitest file result for $($entry.path), found $($matches.Count)."
        }
        $assertions = @($matches[0].assertionResults)
        if ($assertions.Count -ne [int]$entry.expected -or
            @($assertions | Where-Object status -ne 'passed').Count -ne 0) {
            throw "Vitest case reconciliation failed for $($entry.path)."
        }
        foreach ($assertion in $assertions) {
            $caseEvidence.Add([ordered]@{
                runner = 'Vitest'
                file = [string]$entry.path
                testKind = [string]$entry.testKind
                case = [string]$assertion.fullName
                status = 'Passed'
            })
        }
    }
    Add-PlaywrightEvidence $BrowserSmokeResult $manifest.browserSmoke 'PlaywrightBrowserSmoke' $caseEvidence
} else {
    Add-PlaywrightEvidence $EndToEndResult $manifest.endToEnd 'PlaywrightRealEndToEnd' $caseEvidence
}

$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Resolve-RepoPath "artifacts/test-results/cloud-web-$($Mode.ToLowerInvariant())-inventory.json"
} else {
    Resolve-RepoPath $OutputPath
}
New-Item -ItemType Directory -Path (Split-Path $resolvedOutput -Parent) -Force | Out-Null
$runnerCount = @($caseEvidence.runner | Sort-Object -Unique).Count
$fileCount = @($caseEvidence.file | Sort-Object -Unique).Count
[ordered]@{
    schemaVersion = 2
    mode = $Mode
    runners = $runnerCount
    files = $fileCount
    cases = $caseEvidence.Count
    failed = 0
    skipped = 0
    testKinds = @($caseEvidence | Group-Object { [string]$_['testKind'] } | Sort-Object Name | ForEach-Object {
        [ordered]@{
            testKind = $_.Name
            cases = $_.Count
            files = @($_.Group | ForEach-Object { [string]$_['file'] } | Sort-Object -Unique).Count
        }
    })
    evidence = $caseEvidence
} | ConvertTo-Json -Depth 20 | Set-Content $resolvedOutput -Encoding utf8

Write-Host "CLOUD_WEB_INVENTORY_OK mode=$Mode runners=$runnerCount files=$fileCount cases=$($caseEvidence.Count) failed=0 skipped=0 output=$resolvedOutput"
