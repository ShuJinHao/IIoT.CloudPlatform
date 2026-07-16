[CmdletBinding()]
param(
    [string]$CloudRepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [string]$AiRepositoryRoot = $env:AICOPILOT_REPOSITORY_ROOT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExactGitRoot([string]$candidate, [string]$solutionFile, [string]$label) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw "$label repository root was not provided."
    }
    $resolved = (Resolve-Path $candidate).Path
    if (-not (Test-Path (Join-Path $resolved $solutionFile) -PathType Leaf)) {
        throw "$label repository root does not contain ${solutionFile}: $resolved"
    }
    $gitRoot = (& git -C $resolved rev-parse --show-toplevel 2>&1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "$label repository is not a readable Git worktree: $resolved"
    }
    $canonicalGitRoot = (Resolve-Path $gitRoot).Path
    if (-not [string]::Equals($resolved.TrimEnd('/', '\'), $canonicalGitRoot.TrimEnd('/', '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "$label repository root must be the exact Git worktree root: candidate=$resolved actual=$canonicalGitRoot"
    }
    return $resolved
}

function Get-GitHead([string]$repositoryRoot, [string]$label) {
    $head = (& git -C $repositoryRoot rev-parse HEAD 2>&1).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw "$label HEAD is not a full Git SHA: $head"
    }
    return $head
}

function Get-SourceSetDigest([string]$repositoryRoot, [string[]]$relativePaths, [string]$label) {
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($relativePath in @($relativePaths | Sort-Object -CaseSensitive)) {
            $normalized = $relativePath.Replace('\', '/')
            $sourcePath = Join-Path $repositoryRoot $normalized
            if (-not (Test-Path $sourcePath -PathType Leaf)) {
                throw "$label contract source is missing: $sourcePath"
            }
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes("$normalized`n"))
            $hash.AppendData([IO.File]::ReadAllBytes($sourcePath))
            $hash.AppendData([byte[]]@(10))
        }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally {
        $hash.Dispose()
    }
}

function ConvertTo-PathBase64([string]$path) {
    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($path))
}

$cloudRoot = Resolve-ExactGitRoot $CloudRepositoryRoot 'IIoT.CloudPlatform.slnx' 'Cloud'
if ([string]::IsNullOrWhiteSpace($AiRepositoryRoot)) {
    $AiRepositoryRoot = Join-Path (Split-Path $cloudRoot -Parent) 'AICopilot'
}
$aiRoot = Resolve-ExactGitRoot $AiRepositoryRoot 'AICopilot.slnx' 'AICopilot'

$cloudContractSources = @(
    'src/hosts/IIoT.HttpApi/Controllers/AiRead/AiReadController.cs',
    'src/services/IIoT.Services.Contracts/Contracts/AiRead/AiReadResponseContracts.cs',
    'src/services/IIoT.Services.Contracts/Contracts/Authorization/AiReadPermissions.cs',
    'src/services/IIoT.Services.Contracts/Contracts/Identity/IIoTClaimTypes.cs'
)
$aiContractSources = @(
    'src/infrastructure/AICopilot.Infrastructure/CloudRead/CloudAiReadClient.cs',
    'src/services/AICopilot.Services.Contracts/Contracts/CloudAiReadContracts.cs',
    'src/tests/AICopilot.InProcessTests/CloudAiReadClientContractTests.cs',
    'src/tests/AICopilot.CloudAiReadLiveTests/CloudAiReadLiveContractTests.cs'
)

$marker = @(
    'CLOUD_AI_WORKSPACE_EVIDENCE',
    "cloud_root_b64=$(ConvertTo-PathBase64 $cloudRoot)",
    "cloud_head=$(Get-GitHead $cloudRoot 'Cloud')",
    "cloud_contract_sha256=$(Get-SourceSetDigest $cloudRoot $cloudContractSources 'Cloud')",
    "ai_root_b64=$(ConvertTo-PathBase64 $aiRoot)",
    "ai_head=$(Get-GitHead $aiRoot 'AICopilot')",
    "ai_contract_sha256=$(Get-SourceSetDigest $aiRoot $aiContractSources 'AICopilot')"
) -join ' '

$markerPattern = '^CLOUD_AI_WORKSPACE_EVIDENCE cloud_root_b64=[A-Za-z0-9+/]+={0,2} cloud_head=[0-9a-f]{40} cloud_contract_sha256=[0-9a-f]{64} ai_root_b64=[A-Za-z0-9+/]+={0,2} ai_head=[0-9a-f]{40} ai_contract_sha256=[0-9a-f]{64}$'
if ($marker -notmatch $markerPattern) {
    throw 'Cloud/AICopilot workspace evidence marker has an invalid shape.'
}

Write-Output $marker
