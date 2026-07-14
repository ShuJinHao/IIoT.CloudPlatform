[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Required', 'EndToEnd', 'WorkspaceAlignment')]
    [string]$Mode = 'Required',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [string]$ResultsDirectory = 'artifacts/test-results'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$manifestPath = Join-Path $repoRoot 'src/tests/cloud-test-inventory.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -Depth 100
$resultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Resolve-RepoPath([string]$relativePath) {
    return Join-Path $repoRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-IsTestProject([string]$projectPath) {
    [xml]$project = Get-Content $projectPath -Raw
    return @($project.Project.PropertyGroup.IsTestProject) -contains 'true'
}

function Get-DiscoveredCases($runner) {
    $projectPath = Resolve-RepoPath $runner.project
    $arguments = @(
        'test', $projectPath,
        '-c', $Configuration,
        '--list-tests',
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse'
    )
    if ($NoBuild) {
        $arguments += @('--no-build', '--no-restore')
    }

    $output = @(& dotnet @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Discovery failed for $($runner.assembly):`n$($output -join [Environment]::NewLine)"
    }

    $markerIndex = -1
    for ($index = 0; $index -lt $output.Count; $index++) {
        if ([string]$output[$index] -match 'Tests are available|测试可用') {
            $markerIndex = $index
            break
        }
    }
    if ($markerIndex -lt 0) {
        throw "Discovery output for $($runner.assembly) did not contain the test-list marker."
    }

    return @($output[($markerIndex + 1)..($output.Count - 1)] |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -match '^IIoT\.' -and $_ -notmatch '\s->\s' })
}

function Resolve-SourceRule($runner, [string]$caseName) {
    $matches = @($runner.sources | Where-Object {
        $caseName -match "\.$([regex]::Escape([string]$_.class))\."
    })
    if ($matches.Count -ne 1) {
        throw "Case '$caseName' in $($runner.assembly) matched $($matches.Count) source rules."
    }
    return $matches[0]
}

function Resolve-CaseFile($source, [string]$caseName) {
    $candidates = @([string]$source.file)
    if ($null -ne $source.PSObject.Properties['additionalFiles']) {
        $candidates += @($source.additionalFiles | ForEach-Object { [string]$_ })
    }
    if ($candidates.Count -eq 1) {
        return $candidates[0]
    }

    $classMarker = ".$([string]$source.class)."
    $methodPart = $caseName.Substring($caseName.IndexOf($classMarker, [StringComparison]::Ordinal) + $classMarker.Length)
    $methodName = ($methodPart -split '[\(\[]', 2)[0]
    $methodPattern = "\b$([regex]::Escape($methodName))\s*\("
    $methodFiles = @($candidates | Where-Object {
        (Get-Content (Resolve-RepoPath $_) -Raw) -match $methodPattern
    })
    if ($methodFiles.Count -ne 1) {
        throw "Case '$caseName' matched $($methodFiles.Count) source files."
    }
    return $methodFiles[0]
}

$runnerProjects = @($manifest.runners | ForEach-Object { [string]$_.project } | Sort-Object)
$actualTestProjects = @(Get-ChildItem (Join-Path $repoRoot 'src/tests') -Filter '*.csproj' -Recurse |
    Where-Object { Get-IsTestProject $_.FullName } |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
if (($runnerProjects -join "`n") -ne ($actualTestProjects -join "`n")) {
    throw "Test runner manifest mismatch.`nManifest:`n$($runnerProjects -join "`n")`nActual:`n$($actualTestProjects -join "`n")"
}

$supportProjects = @($manifest.supportAllowlist | ForEach-Object { [string]$_.project } | Sort-Object)
$actualSupportProjects = @(Get-ChildItem (Join-Path $repoRoot 'src/testing') -Filter '*.csproj' -Recurse |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
if (($supportProjects -join "`n") -ne ($actualSupportProjects -join "`n")) {
    throw "Support allowlist mismatch."
}
foreach ($support in $manifest.supportAllowlist) {
    $supportProject = Resolve-RepoPath $support.project
    if (Get-IsTestProject $supportProject) {
        throw "Support project must not be a test runner: $($support.project)"
    }
    $supportDirectory = Split-Path $supportProject -Parent
    $supportCaseMatches = @(Get-ChildItem $supportDirectory -Filter '*.cs' -Recurse |
        Select-String -Pattern '\[(Fact|Theory)(Attribute)?(?:\(|\])' |
        Where-Object { $_.Path -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' })
    if ($supportCaseMatches.Count -gt 0) {
        throw "Support project contains test cases: $($support.project)"
    }
}

$forbiddenBuckets = @(
    'IIoT.' + 'ServiceLayer' + '.Tests',
    'IIoT.' + 'ProductionService' + '.Tests',
    'IIoT.' + 'Infrastructure' + '.Tests',
    'IIoT.' + 'RedisIntegration' + 'Tests',
    'IIoT.' + 'EndToEnd' + 'Tests'
)
$activeText = @(Get-ChildItem (Join-Path $repoRoot 'src') -Include '*.csproj', '*.cs' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' } |
    ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
foreach ($bucket in $forbiddenBuckets) {
    if ($activeText.Contains($bucket, [StringComparison]::Ordinal)) {
        throw "Retired test bucket is still referenced in active source/project files: $bucket"
    }
}
if ($activeText -match '<Compile[^>]+Link\s*=') {
    throw 'Linked Compile source is forbidden in the Cloud test architecture.'
}
$testSourceText = @(Get-ChildItem (Join-Path $repoRoot 'src/tests') -Filter '*.cs' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' } |
    ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
if ($testSourceText -match 'Skip\s*=|\[(Fact|Theory)\s*\([^\]]*Skip') {
    throw 'Cloud test source still contains a conditional or unconditional Skip.'
}

$productionProjects = @(Get-ChildItem (Join-Path $repoRoot 'src') -Filter '*.csproj' -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]src[\\/](tests|testing)[\\/]' })
foreach ($project in $productionProjects) {
    $projectSource = Get-Content $project.FullName -Raw
    if ($projectSource -match '<ProjectReference[^>]+(?:src[\\/](?:tests|testing)|TestKit)') {
        throw "Production project references tests/TestKit: $($project.FullName)"
    }
}

$expectedTotal = [int](($manifest.runners | Measure-Object -Property expected -Sum).Sum)
$expectedRequired = [int](($manifest.runners | Where-Object required | Measure-Object -Property expected -Sum).Sum)
if ($expectedTotal -ne [int]$manifest.baselineCases -or $expectedRequired -ne [int]$manifest.requiredCases) {
    throw "Manifest totals are inconsistent: total=$expectedTotal required=$expectedRequired."
}

$caseInventory = [System.Collections.Generic.List[object]]::new()
foreach ($runner in $manifest.runners) {
    $sourceTotal = [int](($runner.sources | Measure-Object -Property expected -Sum).Sum)
    if ($sourceTotal -ne [int]$runner.expected) {
        throw "Source count mismatch for $($runner.assembly): sources=$sourceTotal runner=$($runner.expected)"
    }
    foreach ($source in $runner.sources) {
        $sourceFiles = @([string]$source.file)
        if ($null -ne $source.PSObject.Properties['additionalFiles']) {
            $sourceFiles += @($source.additionalFiles | ForEach-Object { [string]$_ })
        }
        foreach ($sourceFile in $sourceFiles) {
            if (-not (Test-Path (Resolve-RepoPath $sourceFile) -PathType Leaf)) {
                throw "Inventory source does not exist: $sourceFile"
            }
        }
    }

    $cases = @(Get-DiscoveredCases $runner)
    if ($cases.Count -ne [int]$runner.expected) {
        throw "Discovery count mismatch for $($runner.assembly): expected=$($runner.expected) discovered=$($cases.Count)"
    }
    foreach ($source in $runner.sources) {
        $sourceCases = @($cases | Where-Object { $_ -match "\.$([regex]::Escape([string]$source.class))\." })
        if ($sourceCases.Count -ne [int]$source.expected) {
            throw "Source discovery mismatch for $($runner.assembly)/$($source.class): expected=$($source.expected) discovered=$($sourceCases.Count)"
        }
    }

    foreach ($caseName in $cases) {
        $source = Resolve-SourceRule $runner $caseName
        $caseInventory.Add([ordered]@{
            assembly = [string]$runner.assembly
            case = $caseName
            class = [string]$source.class
            file = Resolve-CaseFile $source $caseName
            testKind = [string]$runner.testKind
            runtime = if ($null -ne $source.PSObject.Properties['runtime']) { [string]$source.runtime } else { [string]$runner.runtime }
            runtimeDependencies = if ($null -ne $source.PSObject.Properties['runtimeDependencies']) { @($source.runtimeDependencies) } else { @() }
            capability = if ($null -ne $source.PSObject.Properties['capability']) { [string]$source.capability } else { [string]$runner.capability }
            risk = if ($null -ne $source.PSObject.Properties['risk']) { [string]$source.risk } else { [string]$runner.risk }
            concern = if ($null -ne $source.PSObject.Properties['concern']) { [string]$source.concern } else { [string]$runner.concern }
            cadence = [string]$runner.cadence
            profile = [string]$runner.profile
            owner = [string]$runner.owner
            regressionId = if ($null -ne $source.PSObject.Properties['regressionId']) { [string]$source.regressionId } else { $null }
            required = [bool]$runner.required
            skip = $false
            recentStatus = 'NotScheduled'
        })
    }
}
if ($caseInventory.Count -ne [int]$manifest.baselineCases) {
    throw "Case inventory mismatch: expected=$($manifest.baselineCases) actual=$($caseInventory.Count)"
}

$selectedRunners = @(switch ($Mode) {
    'Required' { @($manifest.runners | Where-Object required) }
    'EndToEnd' { @($manifest.runners | Where-Object assembly -eq 'IIoT.CloudPlatform.EndToEndTests') }
    'WorkspaceAlignment' { @($manifest.runners | Where-Object assembly -eq 'IIoT.CloudPlatform.WorkspaceAlignmentTests') }
    default { @() }
})

$workspaceEvidenceMarker = $null
if ($Mode -eq 'WorkspaceAlignment') {
    $evidenceOutput = @(& (Join-Path $PSScriptRoot 'Get-CloudAiWorkspaceEvidence.ps1') -CloudRepositoryRoot $repoRoot)
    if ($LASTEXITCODE -ne 0) {
        throw 'Cloud/AICopilot workspace evidence generation failed.'
    }
    $evidenceMarkers = @($evidenceOutput | Where-Object { $_ -match '^CLOUD_AI_WORKSPACE_EVIDENCE ' })
    if ($evidenceMarkers.Count -ne 1) {
        throw "Expected one Cloud/AICopilot workspace evidence marker, found $($evidenceMarkers.Count)."
    }
    $workspaceEvidenceMarker = [string]$evidenceMarkers[0]
    Write-Host $workspaceEvidenceMarker
}

function Start-RunnerExecution($runner) {
    $trxName = "$($runner.assembly).trx"
    $trxPath = Join-Path $resultsRoot $trxName
    Remove-Item $trxPath -Force -ErrorAction SilentlyContinue
    $arguments = @(
        'test', (Resolve-RepoPath $runner.project),
        '-c', $Configuration,
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse',
        '--logger', "trx;LogFileName=$trxName",
        '--results-directory', $resultsRoot
    )
    if ($NoBuild) {
        $arguments += @('--no-build', '--no-restore')
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    if ([string]$runner.assembly -eq 'IIoT.CloudPlatform.WorkspaceAlignmentTests') {
        if ([string]::IsNullOrWhiteSpace($workspaceEvidenceMarker)) {
            throw 'WorkspaceAlignment runner requires a precomputed evidence marker.'
        }
        $startInfo.Environment['CLOUD_AI_WORKSPACE_EVIDENCE'] = $workspaceEvidenceMarker
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unable to start test runner $($runner.assembly)."
    }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    return [pscustomobject]@{
        Runner = $runner
        TrxPath = $trxPath
        Process = $process
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Complete-RunnerExecution($execution) {
    $execution.Process.WaitForExit()
    $stdout = $execution.Stdout.GetAwaiter().GetResult()
    $stderr = $execution.Stderr.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        Write-Host $stdout.TrimEnd()
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        Write-Host $stderr.TrimEnd()
    }
    $runner = $execution.Runner
    $trxPath = $execution.TrxPath
    if ($execution.Process.ExitCode -ne 0) {
        throw "Test execution failed for $($runner.assembly) with exit code $($execution.Process.ExitCode)."
    }
    if (-not (Test-Path $trxPath -PathType Leaf)) {
        throw "Missing TRX for $($runner.assembly): $trxPath"
    }

    [xml]$trx = Get-Content $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $notExecuted = [int]$counters.notExecuted
    if ($total -ne [int]$runner.expected -or $passed -ne [int]$runner.expected -or
        $failed -ne 0 -or $notExecuted -ne 0) {
        throw "$($runner.assembly) reconciliation failed: expected=$($runner.expected) total=$total passed=$passed failed=$failed notExecuted=$notExecuted"
    }
    foreach ($case in $caseInventory | Where-Object assembly -eq $runner.assembly) {
        $case.recentStatus = 'Passed'
    }
    Write-Host "CLOUD_TEST_RUNNER_OK assembly=$($runner.assembly) discovered=$total executed=$passed failed=0 skipped=0"
}

$parallelRunners = @($selectedRunners | Where-Object executionGroup -eq 'Pure')
$isolatedRunners = @($selectedRunners | Where-Object executionGroup -ne 'Pure')
$parallelExecutions = @($parallelRunners | ForEach-Object { Start-RunnerExecution $_ })
foreach ($execution in $parallelExecutions) {
    Complete-RunnerExecution $execution
}
foreach ($runner in $isolatedRunners) {
    $execution = Start-RunnerExecution $runner
    Complete-RunnerExecution $execution
}

$inventoryOutput = Join-Path $resultsRoot 'cloud-test-inventory.json'
[ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    mode = $Mode
    baselineCases = [int]$manifest.baselineCases
    requiredCases = [int]$manifest.requiredCases
    support = @($manifest.supportAllowlist | ForEach-Object {
        [ordered]@{ assembly = $_.assembly; project = $_.project; caseCount = 0 }
    })
    cases = $caseInventory
} | ConvertTo-Json -Depth 20 | Set-Content $inventoryOutput -Encoding utf8

$executed = if ($selectedRunners.Count -eq 0) {
    0
} else {
    [int](($selectedRunners | Measure-Object -Property expected -Sum).Sum)
}
Write-Host "CLOUD_TEST_INVENTORY_OK baseline=$($manifest.baselineCases) required=$($manifest.requiredCases) selected=$executed support=2 skipped=0 output=$inventoryOutput"
