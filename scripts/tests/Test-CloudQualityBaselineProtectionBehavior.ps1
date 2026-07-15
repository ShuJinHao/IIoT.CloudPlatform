[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CloudQualityBaselineProtection.ps1')

function Invoke-FixtureGit {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = @(& git -C $Repository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: git $($Arguments -join ' '): $($output -join ' ')"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

function Assert-FailsWith {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Label
    )

    $failure = $null
    try {
        & $Action
    }
    catch {
        $failure = $_.Exception.Message
    }
    if ($failure -notmatch $Pattern) {
        throw "$Label did not fail closed with '$Pattern': $failure"
    }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "iiot-cloud-quality-baseline-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
try {
    Invoke-FixtureGit $fixtureRoot @('init', '--initial-branch=main') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('config', 'user.name', 'Cloud Quality Fixture') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('config', 'user.email', 'cloud-quality-fixture@example.invalid') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('config', 'commit.gpgsign', 'false') | Out-Null

    $baselineDirectory = Join-Path $fixtureRoot 'scripts/tests/baselines'
    New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
    Set-Content (Join-Path $baselineDirectory 'existing.json') '{"value":"base"}' -Encoding utf8
    Invoke-FixtureGit $fixtureRoot @('add', '.') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('commit', '-m', 'base') | Out-Null
    $baseCommit = Invoke-FixtureGit $fixtureRoot @('rev-parse', 'HEAD')

    Invoke-FixtureGit $fixtureRoot @('switch', '-c', 'sibling') | Out-Null
    Set-Content (Join-Path $fixtureRoot 'sibling.txt') 'sibling' -Encoding utf8
    Invoke-FixtureGit $fixtureRoot @('add', '.') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('commit', '-m', 'sibling') | Out-Null
    $siblingCommit = Invoke-FixtureGit $fixtureRoot @('rev-parse', 'HEAD')

    Invoke-FixtureGit $fixtureRoot @('switch', 'main') | Out-Null
    Set-Content (Join-Path $baselineDirectory 'existing.json') '{"value":"candidate"}' -Encoding utf8
    Set-Content (Join-Path $baselineDirectory 'initial.json') '{"value":"current-initial"}' -Encoding utf8
    Invoke-FixtureGit $fixtureRoot @('add', '.') | Out-Null
    Invoke-FixtureGit $fixtureRoot @('commit', '-m', 'candidate') | Out-Null
    $headCommit = Invoke-FixtureGit $fixtureRoot @('rev-parse', 'HEAD')

    Assert-FailsWith `
        { Resolve-CloudQualityBaseCommit -RepoRoot $fixtureRoot -BaseRef ('0' * 40) } `
        'non-zero quality BaseRef' `
        'zero BaseRef fixture'
    Assert-FailsWith `
        { Resolve-CloudQualityBaseCommit -RepoRoot $fixtureRoot -BaseRef $headCommit } `
        'pre-change commit' `
        'same-HEAD fixture'
    Assert-FailsWith `
        { Resolve-CloudQualityBaseCommit -RepoRoot $fixtureRoot -BaseRef $siblingCommit } `
        'must be an ancestor' `
        'non-ancestor fixture'

    $resolvedBase = Resolve-CloudQualityBaseCommit -RepoRoot $fixtureRoot -BaseRef $baseCommit
    if ($resolvedBase -cne $baseCommit) {
        throw "Ancestor fixture resolved the wrong base commit: $resolvedBase"
    }

    $script:CloudQualityBaselineBootstrapUsed = $false
    $existing = Get-CloudQualityBaseJson `
        -RepoRoot $fixtureRoot `
        -BaseCommit $baseCommit `
        -RelativePath 'scripts/tests/baselines/existing.json'
    if ([string]$existing.value -cne 'base' -or $script:CloudQualityBaselineBootstrapUsed) {
        throw 'Existing base baseline fixture did not read the pre-change JSON.'
    }

    $script:CloudQualityBaselineBootstrapUsed = $false
    $initial = Get-CloudQualityBaseJson `
        -RepoRoot $fixtureRoot `
        -BaseCommit $baseCommit `
        -RelativePath 'scripts/tests/baselines/initial.json'
    if ([string]$initial.value -cne 'current-initial' -or -not $script:CloudQualityBaselineBootstrapUsed) {
        throw 'Initial baseline fixture did not use the exact current candidate JSON.'
    }

    Assert-FailsWith `
        {
            Get-CloudQualityBaseJson `
                -RepoRoot $fixtureRoot `
                -BaseCommit $baseCommit `
                -RelativePath 'scripts/tests/baselines/missing.json'
        } `
        'candidate baseline is missing' `
        'missing candidate baseline fixture'

    Write-Host 'CLOUD_QUALITY_BASELINE_PROTECTION_OK scenarios=6 basePresent=1 initialExactCandidate=1 sameHeadRejected=1 zeroRejected=1 nonAncestorRejected=1 candidateMissingRejected=1'
}
finally {
    Remove-Item $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
