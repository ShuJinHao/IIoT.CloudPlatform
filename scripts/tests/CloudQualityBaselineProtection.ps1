Set-StrictMode -Version Latest

function Resolve-CloudQualityBaseCommit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$BaseRef
    )

    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        throw 'An immutable quality BaseRef is required.'
    }

    $resolvedOutput = @(& git -C $RepoRoot rev-parse --verify "$BaseRef`^{commit}" 2>&1)
    $resolved = ($resolvedOutput -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $resolved -notmatch '^[0-9a-f]{40}$') {
        throw "Unable to resolve quality BaseRef '$BaseRef' to an immutable commit: $($resolvedOutput -join ' ')"
    }

    $headOutput = @(& git -C $RepoRoot rev-parse --verify 'HEAD^{commit}' 2>&1)
    $head = ($headOutput -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw "Unable to resolve candidate HEAD: $($headOutput -join ' ')"
    }
    if ([string]::Equals($resolved, $head, [StringComparison]::Ordinal)) {
        throw 'Quality BaseRef must identify the pre-change commit, not candidate HEAD.'
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
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read immutable base baseline '$RelativePath' at $BaseCommit`: $($content -join ' ')"
    }

    try {
        return ($content -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Immutable base baseline '$RelativePath' is not valid JSON at $BaseCommit`: $($_.Exception.Message)"
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
