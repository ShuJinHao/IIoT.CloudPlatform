Set-StrictMode -Version Latest
$script:CloudQualityBaselineBootstrapUsed = $false

function Resolve-CloudQualityBaseCommit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$BaseRef
    )

    if ([string]::IsNullOrWhiteSpace($BaseRef) -or
        $BaseRef -match '^0{40}$') {
        throw 'A non-zero quality BaseRef is required.'
    }

    $resolvedOutput = @(& git -C $RepoRoot rev-parse --verify "$BaseRef`^{commit}" 2>&1)
    $resolved = ($resolvedOutput -join '').Trim()
    $resolveExitCode = $LASTEXITCODE
    if ($resolveExitCode -ne 0 -or $resolved -notmatch '^[0-9a-f]{40}$') {
        $global:LASTEXITCODE = 0
        throw "Unable to resolve quality BaseRef '$BaseRef' to a commit: $($resolvedOutput -join ' ')"
    }

    $headOutput = @(& git -C $RepoRoot rev-parse --verify 'HEAD^{commit}' 2>&1)
    $head = ($headOutput -join '').Trim()
    $headExitCode = $LASTEXITCODE
    if ($headExitCode -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        $global:LASTEXITCODE = 0
        throw "Unable to resolve candidate HEAD: $($headOutput -join ' ')"
    }
    if ([string]::Equals($resolved, $head, [StringComparison]::Ordinal)) {
        throw 'Quality BaseRef must identify the pre-change commit, not candidate HEAD.'
    }

    & git -C $RepoRoot merge-base --is-ancestor $resolved $head 2>$null
    $mergeBaseExitCode = $LASTEXITCODE
    if ($mergeBaseExitCode -ne 0) {
        $global:LASTEXITCODE = 0
        throw "Quality BaseRef must be an ancestor of candidate HEAD: base=$resolved head=$head"
    }

    return $resolved
}

function Get-CloudQualityBaseJson {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$BaseCommit,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ($RelativePath -notmatch '^[A-Za-z0-9._/-]+\.json$' -or $RelativePath.Contains('..')) {
        throw "Invalid quality baseline repository path: $RelativePath"
    }

    $content = @(& git -C $RepoRoot show "$BaseCommit`:$RelativePath" 2>&1)
    $showExitCode = $LASTEXITCODE
    if ($showExitCode -ne 0) {
        $global:LASTEXITCODE = 0
        $candidatePath = Join-Path $RepoRoot $RelativePath
        if (-not (Test-Path $candidatePath -PathType Leaf)) {
            throw "Base commit has no '$RelativePath' and candidate baseline is missing: $candidatePath"
        }
        $content = @(Get-Content $candidatePath)
        $script:CloudQualityBaselineBootstrapUsed = $true
        Write-Host "CLOUD_QUALITY_BASELINE_BOOTSTRAP base=$BaseCommit candidate=$RelativePath"
    }

    try {
        return ($content -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Quality baseline '$RelativePath' is not valid JSON: $($_.Exception.Message)"
    }
}

function Assert-CloudQualityAtLeast {
    param(
        [Parameter(Mandatory)][double]$Candidate,
        [Parameter(Mandatory)][double]$Base,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Candidate + 0.000000001 -lt $Base) {
        throw "Quality baseline weakens $Label`: base=$Base candidate=$Candidate"
    }
}

function Assert-CloudQualityAtMost {
    param(
        [Parameter(Mandatory)][double]$Candidate,
        [Parameter(Mandatory)][double]$Base,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Candidate -gt $Base + 0.000000001) {
        throw "Quality baseline weakens $Label`: base=$Base candidate=$Candidate"
    }
}

function Assert-CloudCoverageObservation {
    param(
        [Parameter(Mandatory)][object]$Actual,
        [Parameter(Mandatory)][object]$Floor
    )

    if ([int]$Actual.linesValid -ne [int]$Floor.linesValid -or
        [int]$Actual.branchesValid -ne [int]$Floor.branchesValid) {
        throw "Coverage structural universe mismatch: floor lines=$($Floor.linesValid) branches=$($Floor.branchesValid); actual lines=$($Actual.linesValid) branches=$($Actual.branchesValid)."
    }

    Assert-CloudQualityAtLeast ([int]$Actual.linesCovered) ([int]$Floor.linesCovered) 'observed merged line coverage floor'
    Assert-CloudQualityAtLeast ([int]$Actual.branchesCovered) ([int]$Floor.branchesCovered) 'observed merged branch coverage floor'
    Assert-CloudQualityAtLeast ([double]$Actual.lineRate) ([double]$Floor.lineRate) 'observed merged line-rate floor'
    Assert-CloudQualityAtLeast ([double]$Actual.branchRate) ([double]$Floor.branchRate) 'observed merged branch-rate floor'
}

function Assert-CloudQualityExactSet {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Candidate,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Base,
        [Parameter(Mandatory)][string]$Label
    )

    $candidateSet = @($Candidate | Sort-Object -Unique)
    $baseSet = @($Base | Sort-Object -Unique)
    if ($candidateSet.Count -ne $Candidate.Count -or
        $baseSet.Count -ne $Base.Count -or
        ($candidateSet -join "`n") -cne ($baseSet -join "`n")) {
        $removed = @($baseSet | Where-Object { $_ -notin $candidateSet })
        $added = @($candidateSet | Where-Object { $_ -notin $baseSet })
        throw "Quality exact set changed for $Label`: removed=$($removed -join ',') added=$($added -join ',')."
    }
}

function Assert-CloudQualityContainsBaseSet {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Candidate,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Base,
        [Parameter(Mandatory)][string]$Label
    )

    $candidateSet = @($Candidate | Sort-Object -Unique)
    $baseSet = @($Base | Sort-Object -Unique)
    $removed = @($baseSet | Where-Object { $_ -notin $candidateSet })
    if ($candidateSet.Count -ne $Candidate.Count -or
        $baseSet.Count -ne $Base.Count -or
        $removed.Count -gt 0) {
        throw "Quality candidate removed base members for $Label`: $($removed -join ',')."
    }
}
