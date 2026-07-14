[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Validate', 'Describe')]
    [string]$Mode = 'Validate',
    [string]$RepositoryRoot,
    [Parameter(Mandatory)]
    [string]$TrustedBaseRevision,
    [string]$CandidateRevision = 'HEAD',
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [string]$OutputPath,
    [string]$MigrationId,
    [string]$RuleIdsCsv,
    [string]$Owner,
    [string]$ApprovedBy,
    [string]$Reason,
    [string]$IssuedAtUtc,
    [string]$ExpiresAtUtc,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RuleId = 'CLOUD-BASELINE-MIG-001'
$script:ReceiptSchemaVersion = '1.0'
$script:BaselinePath = 'scripts/tests/baselines/cloud-test-governance.baseline.json'
$script:PolicyPath = 'scripts/tests/TestCloudTestGovernancePolicy.ps1'
$script:PendingRoot = 'scripts/governance/migrations/pending/'
$script:ConsumedRoot = 'scripts/governance/migrations/consumed/'
$script:CancelledRoot = 'scripts/governance/migrations/cancelled/'
$script:ValidatorPath = 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'
$script:TrustedWrapperPath = 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
$script:SelfTestPath = 'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1'
$script:SchemaPath = 'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json'
$script:CanonicalWorkflowPath = '.github/workflows/cloud-ci.yml'
$script:CanonicalWorkflowName = 'cloud-ci'
$script:RequiredFinalJobId = 'required-final'
$script:WorkflowIdentityKeyPattern = '^[A-Za-z_][A-Za-z0-9_-]*$'
$script:WorkflowIdentityValuePattern = '^[A-Za-z0-9](?:[A-Za-z0-9._ /-]{0,126}[A-Za-z0-9._/-])?$'
$script:ReservedWorkflowTrustReferenceTokens = @(
    'cloud-ci',
    'migration-validator-selftest',
    'build-test',
    'required-final',
    'CLOUD-BASELINE-MIG-TRUSTED-EXECUTOR-V1',
    'CLOUD-BASELINE-MIG-ISOLATED-SELFTEST-V1',
    'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1',
    'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1',
    'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1',
    'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json'
)
$script:RequiredWorkflowSuffixSha256 = '1157f363ab219f973cb36bc01d1dae4b9d9c6fdcf14d18fce091d69afd382244'
$script:RequiredTrustAssetPaths = @(
    $script:TrustedWrapperPath,
    $script:SelfTestPath,
    $script:ValidatorPath,
    $script:SchemaPath
)
$script:ImmutableTrustProofPaths = @(
    $script:TrustedWrapperPath,
    $script:SelfTestPath,
    $script:SchemaPath
)
$script:MaximumReceiptLifetime = [TimeSpan]::FromDays(7)
$script:MaximumReceiptBytes = 1MB
$script:MaximumReceiptChanges = 5000
$script:JsonSerializerOptions = [Text.Json.JsonSerializerOptions]::new()
$script:ApprovedOwners = @(
    'Cloud.Architecture',
    'Cloud.Deployment',
    'Cloud.Infrastructure',
    'Cloud.Persistence',
    'Cloud.Security',
    'Cloud.Tests'
)
$script:ApprovedApprovers = @('ShuJinHao')
$script:DescribeRequiredArgumentNames = @(
    'MigrationId',
    'RuleIdsCsv',
    'Owner',
    'ApprovedBy',
    'Reason'
)
$script:DescribeAnchorRelationship = 'BaseAncestorOfHead'
$script:TrustUpgradeRuleId = 'CLOUD-BASELINE-TRUST-UPGRADE-001'
$script:ApprovedGovernedRuleIds = @(
    'CLOUD-TEST-GOV-001B',
    'CLOUD-CACHE-001',
    'CLOUD-ARCH-001',
    'CLOUD-TEST-002',
    'CLOUD-TEST-003',
    'CLOUD-TEST-004',
    'CLOUD-TEST-005',
    'CLOUD-TEST-CLEANUP',
    'CLOUD-BASELINE-TRUST-UPGRADE-001'
)
$script:ReceiptCountFields = @(
    'repositoryProjects',
    'testProjects',
    'testSourceFiles',
    'frozenSourceFiles',
    'declarations',
    'executionTemplates',
    'projectedCases',
    'runnerCases',
    'protectedAssets',
    'workflowFiles',
    'projectFiles',
    'buildControlFiles',
    'restoreControlFiles',
    'frontendTestFiles',
    'frontendRunnerCases',
    'deploymentSourceDeclarations',
    'deploymentRunnerCases'
)

function Stop-MigrationValidation {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )

    throw "$($script:RuleId)-$Code $Message"
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function ConvertFrom-StrictUtf8Bytes {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    }
    catch {
        Stop-MigrationValidation -Code $Code -Message "$Context is not valid UTF-8."
    }
}

function Get-OrdinalSortedStrings {
    param(
        [AllowEmptyCollection()][object[]]$Values,
        [switch]$Unique
    )

    $items = [Collections.Generic.List[string]]::new()
    foreach ($value in @($Values)) { $items.Add([string]$value) }
    $items.Sort([StringComparer]::Ordinal)
    if (-not $Unique) { return @($items) }

    $result = [Collections.Generic.List[string]]::new()
    $previous = $null
    foreach ($item in $items) {
        if ($null -eq $previous -or -not [StringComparer]::Ordinal.Equals($previous, $item)) {
            $result.Add($item)
            $previous = $item
        }
    }
    return @($result)
}

function Get-OrdinalSortedChangeRecords {
    param([AllowEmptyCollection()][object[]]$Values)

    $items = [Collections.Generic.List[object]]::new()
    foreach ($value in @($Values)) { $items.Add($value) }
    $comparer = [Collections.Generic.Comparer[object]]::Create(
        [Comparison[object]]{
            param($left, $right)
            return [StringComparer]::Ordinal.Compare([string]$left.path, [string]$right.path)
        })
    $items.Sort($comparer)
    return @($items)
}

function Invoke-GitBytes {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($script:RepositoryRoot)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Stop-MigrationValidation -Code 'GIT' -Message "could not start git $($Arguments -join ' ')."
    }

    $errorTask = $process.StandardError.ReadToEndAsync()
    $buffer = [IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Bytes = $buffer.ToArray()
            Error = $errorText
        }
    }
    finally {
        $buffer.Dispose()
        $process.Dispose()
    }

    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        Stop-MigrationValidation -Code 'GIT' -Message (
            "git {0} failed with exit code {1}: {2}" -f
            ($Arguments -join ' '), $result.ExitCode, $result.Error)
    }

    return $result
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $result = Invoke-GitBytes -Arguments $Arguments -AllowFailure:$AllowFailure
    return [pscustomobject]@{
        ExitCode = $result.ExitCode
        Text = [Text.Encoding]::UTF8.GetString($result.Bytes)
        Error = $result.Error
    }
}

function Resolve-Commit {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Revision) -or $Revision -match '^0{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name must be a non-zero commit revision."
    }

    $result = Invoke-GitText -Arguments @('rev-parse', '--verify', "$Revision^{commit}") -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name is not an available commit: $Revision."
    }

    $resolved = $result.Text.Trim()
    if ($resolved -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'REVISION' -Message "$Name did not resolve to one full commit SHA."
    }

    return $resolved
}

function Assert-Ancestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $result = Invoke-GitText -Arguments @('merge-base', '--is-ancestor', $Ancestor, $Descendant) -AllowFailure
    if ($result.ExitCode -ne 0) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message "$Context requires $Ancestor to be an ancestor of $Descendant."
    }
}

function Assert-FirstParentAncestry {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context
    )

    $history = (Invoke-GitText -Arguments @('rev-list', '--first-parent', $Descendant)).Text
    $firstParentCommits = @($history -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($Ancestor -cnotin $firstParentCommits) {
        Stop-MigrationValidation -Code 'ANCESTRY' -Message (
            "$Context requires $Ancestor on the first-parent chain of $Descendant.")
    }
}

function Assert-LinearHistoryRange {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$Context,
        [switch]$ValidateAncestorEndpoint
    )

    $history = (Invoke-GitText -Arguments @(
        'rev-list', '--parents', "${Ancestor}..${Descendant}")).Text
    $commits = @($history -split "`r?`n" | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($ValidateAncestorEndpoint) {
        $ancestorRecord = (Invoke-GitText -Arguments @(
            'rev-list', '--parents', '-n', '1', $Ancestor)).Text.Trim()
        if ([string]::IsNullOrWhiteSpace($ancestorRecord)) {
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context could not inspect the untrusted ancestor endpoint $Ancestor.")
        }
        $ancestorParts = @($ancestorRecord -split '\s+')
        if ($ancestorParts.Count -gt 2) {
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context requires the untrusted ancestor endpoint $Ancestor to have at most one parent; " +
                "found $($ancestorParts.Count - 1).")
        }
    }
    foreach ($commit in $commits) {
        $parts = @($commit.Trim() -split '\s+')
        if ($parts.Count -ne 2) {
            $commitId = if ($parts.Count -gt 0) { $parts[0] } else { '<unknown>' }
            $parentCount = [Math]::Max(0, $parts.Count - 1)
            Stop-MigrationValidation -Code 'HISTORY' -Message (
                "$Context requires a merge-free linear history range from $Ancestor to $Descendant; " +
                "commit $commitId has $parentCount parent(s).")
        }
    }
}

function Assert-DirectSingleParentTransition {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Context
    )

    $parents = (Invoke-GitText -Arguments @(
        'rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($parents.Count -ne 2 -or $parents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code $Code -Message (
            "$Context must be one single-parent commit directly after its trusted base.")
    }
}

function Assert-SafeRepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -cne $Path.Trim() -or
        $Path.StartsWith('/') -or
        $Path.Contains('\') -or
        $Path.Contains('//') -or
        $Path -match '(^|/)\.\.?(/|$)' -or
        $Path -match '[\x00-\x1F\x7F]' -or
        $Path.Contains('*') -or
        $Path.Contains('?') -or
        $Path.Contains('[') -or
        $Path.Contains(']') -or
        $Path -match '[<>:"|]') {
        Stop-MigrationValidation -Code 'PATH' -Message "unsafe repository path '$Path'."
    }

    $windowsReservedName = '^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\..*)?$'
    foreach ($segment in $Path.Split('/')) {
        if ($segment.Length -gt 255 -or
            $segment.EndsWith(' ', [StringComparison]::Ordinal) -or
            $segment.EndsWith('.', [StringComparison]::Ordinal) -or
            $segment -match $windowsReservedName) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "repository path '$Path' is not portable to supported Windows worktrees.")
        }
    }
}

function Get-RevisionPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-r', '-z', '--name-only', $Revision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $text = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'PATH' `
        -Context "repository tree at $Revision"
    $nul = [char[]]@([char]0)
    $paths = @($text.TrimEnd($nul).Split($nul, [StringSplitOptions]::None) |
        Where-Object { $_ -ne '' })
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) {
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding repository paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path
    }

    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Get-GitEntry {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowMissing
    )

    Assert-SafeRepositoryPath -Path $Path
    $result = Invoke-GitBytes -Arguments @(
        '-c', 'core.quotepath=false', 'ls-tree', '-z', $Revision, '--', $Path)
    if ($result.Bytes.Length -eq 0) {
        if ($AllowMissing) { return $null }
        Stop-MigrationValidation -Code 'BLOB' -Message "$Revision has no tracked file '$Path'."
    }

    $text = (ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'BLOB' `
        -Context "git entry for '$Path' at $Revision").TrimEnd([char]0)
    $match = [regex]::Match($text, '^([0-9]{6}) ([a-z]+) ([0-9a-f]+)\t(.+)$')
    if (-not $match.Success -or $match.Groups[4].Value -cne $Path) {
        Stop-MigrationValidation -Code 'BLOB' -Message "could not parse git entry for '$Path' at $Revision."
    }
    if ($match.Groups[2].Value -cne 'blob' -or $match.Groups[1].Value -cnotin @('100644', '100755')) {
        Stop-MigrationValidation -Code 'MODE' -Message (
            "'$Path' uses forbidden git type/mode $($match.Groups[2].Value)/$($match.Groups[1].Value).")
    }

    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        ObjectId = $match.Groups[3].Value
        Path = $Path
    }
}

function Get-GitBlobBytes {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    return (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
}

function Get-GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    return [Text.Encoding]::UTF8.GetString((Get-GitBlobBytes -Revision $Revision -Path $Path))
}

function Get-StrictUtf8GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString(
            (Get-GitBlobBytes -Revision $Revision -Path $Path))
    }
    catch {
        Stop-MigrationValidation -Code 'TRUST' -Message "'$Path' is not valid UTF-8 at $Revision."
    }
}

function Get-OrdinalOccurrenceCount {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Value
    )

    if ($Value.Length -eq 0) { return 0 }
    $count = 0
    $offset = 0
    while ($offset -le $Text.Length - $Value.Length) {
        $index = $Text.IndexOf($Value, $offset, [StringComparison]::Ordinal)
        if ($index -lt 0) { break }
        $count++
        $offset = $index + $Value.Length
    }
    return $count
}

function Assert-NonCanonicalWorkflowIdentityGrammar {
    param(
        [Parameter(Mandatory)][string]$WorkflowPath,
        [Parameter(Mandatory)][string]$WorkflowText
    )

    $text = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = @($text.Split("`n"))
    $workflowName = $null
    $workflowNameCount = 0
    $jobsLineIndex = -1
    $jobsKeyCount = 0

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = [string]$lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or
            $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            continue
        }
        $leadingWhitespace = [regex]::Match($line, '^[ \t]*').Value
        if ($leadingWhitespace.Contains("`t", [StringComparison]::Ordinal)) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' cannot use tab indentation in its identity grammar.")
        }
        $indentMatch = [regex]::Match($line, '^ *')
        if ($indentMatch.Length -ne 0) { continue }
        $topLevelMatch = [regex]::Match(
            $line,
            '^(?<Key>[A-Za-z_][A-Za-z0-9_-]*):(?<Rest>.*)$')
        if (-not $topLevelMatch.Success) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' must use plain block-mapping top-level keys.")
        }
        $key = $topLevelMatch.Groups['Key'].Value
        if ($key -ceq 'name') {
            $workflowNameCount++
            $nameMatch = [regex]::Match(
                $line,
                '^name: (?<Identity>[A-Za-z0-9](?:[A-Za-z0-9._ /-]{0,126}[A-Za-z0-9._/-])?)$')
            if (-not $nameMatch.Success -or
                $nameMatch.Groups['Identity'].Value -cnotmatch $script:WorkflowIdentityValuePattern) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use one plain ASCII workflow name without YAML indirection.")
            }
            $workflowName = $nameMatch.Groups['Identity'].Value
        }
        elseif ($key -ceq 'jobs') {
            $jobsKeyCount++
            if ($line -cne 'jobs:') {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use a plain block-mapping jobs key.")
            }
            $jobsLineIndex = $index
        }
    }

    if ($workflowNameCount -ne 1 -or $jobsKeyCount -ne 1 -or $jobsLineIndex -lt 0) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' must contain exactly one plain name and one plain jobs mapping.")
    }
    if ($workflowName -ceq $script:CanonicalWorkflowName) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' reuses canonical workflow identity '$workflowName'.")
    }

    $jobIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $currentJobId = $null
    $currentJobNameCount = 0
    for ($index = $jobsLineIndex + 1; $index -lt $lines.Count; $index++) {
        $line = [string]$lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or
            $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            continue
        }
        $indentMatch = [regex]::Match($line, '^ *')
        $indent = $indentMatch.Length
        if ($indent -eq 0) { break }
        if ($indent -eq 2) {
            $jobMatch = [regex]::Match(
                $line,
                '^  (?<JobId>[A-Za-z_][A-Za-z0-9_-]*):$')
            if (-not $jobMatch.Success) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use plain ASCII block-mapping job IDs.")
            }
            $currentJobId = $jobMatch.Groups['JobId'].Value
            $currentJobNameCount = 0
            if ($currentJobId -cnotmatch $script:WorkflowIdentityKeyPattern -or
                -not $jobIds.Add($currentJobId)) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' contains an invalid or duplicate job ID '$currentJobId'.")
            }
            if ($currentJobId -ceq $script:RequiredFinalJobId) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' reuses required job identity '$currentJobId'.")
            }
            continue
        }
        if ($indent -lt 4 -or $null -eq $currentJobId) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "non-canonical workflow '$WorkflowPath' has a non-canonical jobs mapping indentation.")
        }
        if ($indent -eq 4) {
            $propertyMatch = [regex]::Match(
                $line,
                '^    (?<Key>[A-Za-z_][A-Za-z0-9_-]*):(?<Rest>.*)$')
            if (-not $propertyMatch.Success) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "non-canonical workflow '$WorkflowPath' must use plain ASCII direct job keys without YAML merge or indirection.")
            }
            if ($propertyMatch.Groups['Key'].Value -ceq 'name') {
                $currentJobNameCount++
                $jobNameMatch = [regex]::Match(
                    $line,
                    '^    name: (?<Identity>[A-Za-z0-9](?:[A-Za-z0-9._ /-]{0,126}[A-Za-z0-9._/-])?)$')
                if ($currentJobNameCount -ne 1 -or
                    -not $jobNameMatch.Success -or
                    $jobNameMatch.Groups['Identity'].Value -cnotmatch $script:WorkflowIdentityValuePattern) {
                    Stop-MigrationValidation -Code 'TRUST' -Message (
                        "non-canonical workflow '$WorkflowPath' must use at most one plain ASCII direct job name without YAML indirection.")
                }
                $jobName = $jobNameMatch.Groups['Identity'].Value
                if ($jobName -ceq $script:RequiredFinalJobId) {
                    Stop-MigrationValidation -Code 'TRUST' -Message (
                        "non-canonical workflow '$WorkflowPath' reuses required check identity '$jobName'.")
                }
            }
        }
    }
    if ($jobIds.Count -eq 0) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "non-canonical workflow '$WorkflowPath' must contain at least one plain job ID.")
    }
}

function Assert-TargetWorkflowTrustClosure {
    param([Parameter(Mandatory)][string]$Revision)

    $workflowPath = $script:CanonicalWorkflowPath
    $requiredBlock = @'
      - name: Validate trusted baseline migration
        shell: pwsh
        run: |
          # CLOUD-BASELINE-MIG-TRUSTED-EXECUTOR-V1
          $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
          $candidate = '${{ github.event.pull_request.head.sha || github.sha }}'
          $eventName = '${{ github.event_name }}'
          $eventRef = '${{ github.ref }}'
          if ($eventName -eq 'workflow_dispatch' -and $eventRef -ne 'refs/heads/main') {
            throw 'workflow_dispatch is trusted only on refs/heads/main.'
          }
          if ($trustedBase -notmatch '^[0-9a-fA-F]{40}$' -or $trustedBase -match '^0{40}$') {
            $trustedBase = (git rev-parse HEAD^ | Out-String).Trim()
          }
          $trustedWrapperPath = 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
          $entry = (git ls-tree $trustedBase -- $trustedWrapperPath | Out-String).Trim()
          $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($trustedWrapperPath) + '$'
          if ($LASTEXITCODE -ne 0 -or $entry -notmatch $entryPattern) {
            throw 'Trusted base does not contain the reviewed migration wrapper.'
          }
          $temporaryWrapper = Join-Path $env:RUNNER_TEMP 'cloud-baseline-migration-wrapper.ps1'
          try {
            & git cat-file blob $Matches.ObjectId > $temporaryWrapper
            if ($LASTEXITCODE -ne 0) { throw 'Could not extract the trusted migration wrapper.' }
            $extractedObjectId = (git hash-object --no-filters -- $temporaryWrapper | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $extractedObjectId -cne $Matches.ObjectId) {
              throw 'Extracted migration wrapper differs from the trusted Git blob.'
            }
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryWrapper `
              -RepositoryRoot . `
              -TrustedBaseRevision $trustedBase `
              -CandidateRevision $candidate `
              -AnchorRelationship BaseAncestorOfHead
            if ($LASTEXITCODE -ne 0) { throw "Trusted migration validation failed with exit code $LASTEXITCODE." }
          }
          finally {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
          }
'@
    $requiredHeader = @'
name: cloud-ci

on:
  workflow_dispatch:
    inputs:
      include_end_to_end:
        description: "Run repository EndToEnd tests; excludes LiveExternal workspace alignment"
        required: false
        type: boolean
        default: false
  push:
    branches: [main]
  pull_request: {}

permissions:
  contents: read

env:
  DOTNET_NOLOGO: true

jobs:
'@
    $selfTestJob = @'
  migration-validator-selftest:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout validator candidate
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}
      - name: Run isolated Cloud baseline migration validator self-tests
        shell: pwsh
        run: |
          # CLOUD-BASELINE-MIG-ISOLATED-SELFTEST-V1
          $trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
          $eventName = '${{ github.event_name }}'
          $eventRef = '${{ github.ref }}'
          if ($eventName -eq 'workflow_dispatch' -and $eventRef -ne 'refs/heads/main') {
            throw 'workflow_dispatch is trusted only on refs/heads/main.'
          }
          if ($trustedBase -notmatch '^[0-9a-fA-F]{40}$' -or $trustedBase -match '^0{40}$') {
            $trustedBase = (git rev-parse HEAD^ | Out-String).Trim()
          }
          $trustedHarnessAssets = @(
            [pscustomobject]@{
              Path = 'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1'
              FileName = 'TestCloudBaselineMigrationValidator.v1.ps1'
            },
            [pscustomobject]@{
              Path = 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
              FileName = 'InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
            },
            [pscustomobject]@{
              Path = 'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json'
              FileName = 'cloud-baseline-migration-receipt.schema.json'
            },
            [pscustomobject]@{
              Path = '.github/workflows/cloud-ci.yml'
              FileName = 'cloud-ci.yml'
            }
          )
          $trustedHarnessDirectory = Join-Path $env:RUNNER_TEMP (
            "cloud-baseline-migration-harness-$([Guid]::NewGuid().ToString('N'))")
          try {
            [IO.Directory]::CreateDirectory($trustedHarnessDirectory) | Out-Null
            foreach ($asset in $trustedHarnessAssets) {
              $entry = (git ls-tree $trustedBase -- $asset.Path | Out-String).Trim()
              $entryPattern = '^100644 blob (?<ObjectId>[0-9a-f]+)\t' + [regex]::Escape($asset.Path) + '$'
              $entryMatch = [regex]::Match($entry, $entryPattern)
              if ($LASTEXITCODE -ne 0 -or -not $entryMatch.Success) {
                throw "Trusted base does not contain reviewed mode-100644 proof asset '$($asset.Path)'."
              }
              $temporaryAsset = Join-Path $trustedHarnessDirectory $asset.FileName
              & git cat-file blob $entryMatch.Groups['ObjectId'].Value > $temporaryAsset
              if ($LASTEXITCODE -ne 0) { throw "Could not extract trusted proof asset '$($asset.Path)'." }
              $extractedObjectId = (git hash-object --no-filters -- $temporaryAsset | Out-String).Trim()
              if ($LASTEXITCODE -ne 0 -or
                  $extractedObjectId -cne $entryMatch.Groups['ObjectId'].Value) {
                throw "Extracted proof asset '$($asset.Path)' differs from the trusted Git blob."
              }
            }
            $temporarySelfTest = Join-Path $trustedHarnessDirectory 'TestCloudBaselineMigrationValidator.v1.ps1'
            $temporaryWrapper = Join-Path $trustedHarnessDirectory 'InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
            $temporarySchema = Join-Path $trustedHarnessDirectory 'cloud-baseline-migration-receipt.schema.json'
            $temporaryWorkflow = Join-Path $trustedHarnessDirectory 'cloud-ci.yml'
            & pwsh -NoLogo -NoProfile -NonInteractive -File $temporarySelfTest `
              -ValidatorPath ./scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1 `
              -TrustedWrapperPath $temporaryWrapper `
              -SchemaPath $temporarySchema `
              -TrustedWorkflowPath $temporaryWorkflow
            if ($LASTEXITCODE -ne 0) { throw "Trusted migration self-test failed with exit code $LASTEXITCODE." }
          }
          finally {
            Remove-Item $trustedHarnessDirectory -Recurse -Force -ErrorAction SilentlyContinue
          }
'@
    $beforeGate = @'
  build-test:
    runs-on: ubuntu-24.04
    timeout-minutes: 25

    steps:
      - name: Checkout
        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7
        with:
          fetch-depth: 0
          persist-credentials: false
          ref: ${{ github.event.pull_request.head.sha || github.sha }}
'@
    $afterGate = @'
      - name: Setup .NET
        uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          global-json-file: global.json
'@

    $text = (Get-StrictUtf8GitBlobText -Revision $Revision -Path $workflowPath).
        Replace("`r`n", "`n").Replace("`r", "`n")
    $requiredBlockText = ([string]$requiredBlock).TrimEnd("`r", "`n")
    $requiredHeaderText = ([string]$requiredHeader).TrimEnd("`r", "`n")
    $selfTestJobText = ([string]$selfTestJob).TrimEnd("`r", "`n")
    $beforeGateText = ([string]$beforeGate).TrimEnd("`r", "`n")
    $afterGateText = ([string]$afterGate).TrimEnd("`r", "`n")
    $requiredPrefix = "$requiredHeaderText`n$selfTestJobText`n`n$beforeGateText`n$requiredBlockText`n"
    $setupStepMarker = "      - name: Setup .NET`n"
    $setupStepIndex = $text.IndexOf($setupStepMarker, [StringComparison]::Ordinal)
    $prefixMatches = $text.StartsWith($requiredPrefix, [StringComparison]::Ordinal)
    $suffixStartsWithSetup = $setupStepIndex -ge 0 -and
        $text.Substring($setupStepIndex).StartsWith($afterGateText, [StringComparison]::Ordinal)
    $actualSuffixSha256 = if ($setupStepIndex -ge 0) {
        Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($text.Substring($setupStepIndex)))
    } else {
        '<missing>'
    }
    if ($setupStepIndex -ne $requiredPrefix.Length -or
        -not $prefixMatches -or
        -not $suffixStartsWithSetup -or
        $actualSuffixSha256 -cne $script:RequiredWorkflowSuffixSha256) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' isolated self-test job, authoritative gate, Phase 0 suffix or job dependencies are not canonical; " +
            "setupIndex=$setupStepIndex expectedPrefixLength=$($requiredPrefix.Length) " +
            "prefixMatches=$prefixMatches suffixStartsWithSetup=$suffixStartsWithSetup " +
            "suffixSha256=$actualSuffixSha256 expectedSuffixSha256=$($script:RequiredWorkflowSuffixSha256).")
    }
    $topLevelLines = @($text.Split("`n") | Where-Object {
        $_ -match '^\S' -and -not $_.StartsWith('#', [StringComparison]::Ordinal)
    })
    $expectedTopLevelLines = @(
        "name: $($script:CanonicalWorkflowName)", 'on:', 'permissions:', 'env:', 'jobs:')
    if (-not $text.StartsWith("$requiredHeaderText`n", [StringComparison]::Ordinal) -or
        ($topLevelLines -join '|') -cne ($expectedTopLevelLines -join '|')) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' trigger, permission, environment and top-level trust envelope are not canonical.")
    }
    if ([regex]::Matches($text, '(?m)^jobs:\s*$').Count -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value "  migration-validator-selftest:`n") -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value "  build-test:`n") -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value "  $($script:RequiredFinalJobId):`n") -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'needs: [migration-validator-selftest, build-test]') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value "needs: $($script:RequiredFinalJobId)") -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value (
            "    timeout-minutes: 1`n" +
            "    steps:`n")) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value (
            "  required-final:`n" +
            "    needs: [migration-validator-selftest, build-test]`n" +
            "    if: always()`n")) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'MIGRATION_VALIDATOR_SELFTEST_RESULT: ${{ needs.migration-validator-selftest.result }}') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'BUILD_TEST_RESULT: ${{ needs.build-test.result }}') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'test "$MIGRATION_VALIDATOR_SELFTEST_RESULT" = success') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'test "$BUILD_TEST_RESULT" = success') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $requiredBlockText) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'CLOUD-BASELINE-MIG-TRUSTED-EXECUTOR-V1') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'CLOUD-BASELINE-MIG-ISOLATED-SELFTEST-V1') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $script:TrustedWrapperPath) -ne 2 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $script:SelfTestPath) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $script:SchemaPath) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $script:ValidatorPath) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value $workflowPath) -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value '-TrustedWorkflowPath $temporaryWorkflow') -ne 1 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'persist-credentials: false') -ne 2 -or
        (Get-OrdinalOccurrenceCount -Text $text -Value 'ref: ${{ github.event.pull_request.head.sha || github.sha }}') -ne 2) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            "'$workflowPath' must contain parallel isolated/base-owned gates, one fail-closed final job, exact dependencies and two credential-free PR-head checkouts.")
    }

    foreach ($otherWorkflowPath in @(Get-RevisionPaths -Revision $Revision | Where-Object {
        $_.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
        [IO.Path]::GetExtension($_) -in @('.yml', '.yaml') -and
        $_ -cne $workflowPath
    })) {
        $otherText = Get-StrictUtf8GitBlobText -Revision $Revision -Path $otherWorkflowPath
        Assert-NonCanonicalWorkflowIdentityGrammar `
            -WorkflowPath $otherWorkflowPath `
            -WorkflowText $otherText
        foreach ($reservedLiteral in $script:ReservedWorkflowTrustReferenceTokens) {
            if ($otherText.Contains($reservedLiteral, [StringComparison]::Ordinal)) {
                Stop-MigrationValidation -Code 'TRUST' -Message (
                    "'$otherWorkflowPath' contains reserved Cloud migration workflow literal '$reservedLiteral'.")
            }
        }
    }
}

function Get-FileState {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path -AllowMissing
    if ($null -eq $entry) {
        return [pscustomobject]@{ Mode = $null; Sha256 = $null }
    }

    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    return [pscustomobject]@{
        Mode = $entry.Mode
        Sha256 = Get-Sha256Hex -Bytes $bytes
    }
}

function Get-DiffRecords {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision
    )

    $result = Invoke-GitBytes -Arguments @(
        'diff-tree', '-r', '--no-commit-id', '--name-status', '--no-renames', '-z',
        $BaseRevision, $TargetRevision)
    if ($result.Bytes.Length -eq 0) {
        return @()
    }

    $nul = [char[]]@([char]0)
    $diffText = ConvertFrom-StrictUtf8Bytes `
        -Bytes $result.Bytes `
        -Code 'DIFF' `
        -Context "git diff $BaseRevision..$TargetRevision"
    $parts = @($diffText.TrimEnd($nul).Split($nul, [StringSplitOptions]::None))
    if ($parts.Count % 2 -ne 0) {
        Stop-MigrationValidation -Code 'DIFF' -Message 'git diff-tree returned an invalid name-status stream.'
    }

    $records = [Collections.Generic.List[object]]::new()
    $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $parts.Count; $index += 2) {
        $status = $parts[$index]
        $path = $parts[$index + 1]
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'DIFF' -Message "unsupported git status '$status' for '$path'."
        }
        Assert-SafeRepositoryPath -Path $path
        if ($caseLedger.ContainsKey($path) -and $caseLedger[$path] -cne $path) {
            Stop-MigrationValidation -Code 'PATH' -Message (
                "case-colliding diff paths '$($caseLedger[$path])' and '$path'.")
        }
        $caseLedger[$path] = $path

        $before = Get-FileState -Revision $BaseRevision -Path $path
        $after = Get-FileState -Revision $TargetRevision -Path $path
        if (($status -ceq 'A' -and ($null -ne $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'M' -and ($null -eq $before.Mode -or $null -eq $after.Mode)) -or
            ($status -ceq 'D' -and ($null -eq $before.Mode -or $null -ne $after.Mode))) {
            Stop-MigrationValidation -Code 'DIFF' -Message "status '$status' disagrees with '$path' blob presence."
        }

        $records.Add([pscustomobject][ordered]@{
            path = $path
            status = $status
            beforeMode = $before.Mode
            beforeSha256 = $before.Sha256
            afterMode = $after.Mode
            afterSha256 = $after.Sha256
        })
    }

    return @(Get-OrdinalSortedChangeRecords -Values @($records))
}

function Test-IsReceiptStatePath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:ConsumedRoot, [StringComparison]::Ordinal) -or
        $Path.StartsWith($script:CancelledRoot, [StringComparison]::Ordinal)
}

function Test-IsTrustUpgradePath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path -ceq $script:ValidatorPath
}

function Test-IsImmutableTrustProofPath {
    param([Parameter(Mandatory)][string]$Path)

    return $script:ImmutableTrustProofPaths -ccontains $Path
}

function Assert-TrustImplementationAssets {
    param([Parameter(Mandatory)][string]$Revision)

    foreach ($path in $script:RequiredTrustAssetPaths) {
        try {
            $entry = Get-GitEntry -Revision $Revision -Path $path
        }
        catch {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' is missing at $Revision.")
        }
        if ($entry.Mode -cne '100644') {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "trusted implementation asset '$path' must use mode 100644 at $Revision.")
        }
    }
}

function Test-IsBuildControlPath {
    param([Parameter(Mandatory)][string]$Path)

    $fileName = [IO.Path]::GetFileName($Path)
    return $fileName -match '(?i)^Directory\.(?:Build|Solution)\.(?:props|targets)$' -or
        $fileName -match '(?i)^(?:before|after)\..+\.sln\.targets$' -or
        $fileName -match '(?i)\.[^.]*proj\.user$' -or
        $Path -match '(?i)(?:^|/)obj/[^/]+\.[^.]*proj\..+\.(?:props|targets)$' -or
        $fileName -ieq '.editorconfig' -or
        $fileName -ieq '.globalconfig' -or
        $fileName -ieq 'runtimeconfig.template.json'
}

function Test-IsRestoreControlPath {
    param([Parameter(Mandatory)][string]$Path)

    $fileName = [IO.Path]::GetFileName($Path)
    return $fileName -ieq 'NuGet.Config' -or
        $fileName -ieq 'Directory.Packages.props' -or
        $fileName -ieq 'packages.lock.json' -or
        $fileName -ieq '.npmrc' -or
        $fileName -ieq 'npm-shrinkwrap.json' -or
        $fileName -ieq 'package-lock.json'
}

function Test-IsFrontendTestPath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.StartsWith('src/ui/iiot-web/', [StringComparison]::Ordinal) -and
        $Path -match '(?i)\.(?:spec|test)\.ts$'
}

function Test-IsProtectedPath {
    param([Parameter(Mandatory)][string]$Path)

    $exactPaths = @(
        '.github/CODEOWNERS',
        '.gitattributes',
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'IIoT.CloudPlatform.slnx',
        $script:PolicyPath,
        'src/ui/iiot-web/package.json',
        'src/ui/iiot-web/package-lock.json',
        'src/ui/iiot-web/vitest.config.ts'
    )
    if ($Path -cin $exactPaths) { return $true }
    if ($Path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('.github/actions/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('scripts/tests/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('scripts/governance/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/Analyzers/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/analyzers/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('src/tests/', [StringComparison]::Ordinal) -or
        $Path.StartsWith('deploy/', [StringComparison]::Ordinal) -or
        $Path.Equals('.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/.editorconfig', [StringComparison]::OrdinalIgnoreCase) -or
        (Test-IsBuildControlPath -Path $Path) -or
        (Test-IsRestoreControlPath -Path $Path) -or
        (Test-IsFrontendTestPath -Path $Path)) {
        return $true
    }

    $extension = [IO.Path]::GetExtension($Path)
    return $extension -in @('.csproj', '.fsproj', '.vbproj', '.slnx', '.props', '.targets', '.rsp')
}

function Get-ProtectedManifestDigest {
    param([Parameter(Mandatory)][string]$Revision)

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($path in (Get-RevisionPaths -Revision $Revision)) {
        if (-not (Test-IsProtectedPath -Path $path) -or (Test-IsReceiptStatePath -Path $path)) {
            continue
        }
        $state = Get-FileState -Revision $Revision -Path $path
        $lines.Add("$($state.Mode)`0$($state.Sha256)`0$path`n")
    }

    return Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes(($lines -join '')))
}

function Get-SolutionProjectPaths {
    param([Parameter(Mandatory)][string]$Revision)

    try {
        [xml]$solution = Get-GitBlobText -Revision $Revision -Path 'IIoT.CloudPlatform.slnx'
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse IIoT.CloudPlatform.slnx at ${Revision}: $($_.Exception.Message)"
    }

    $projects = @($solution.SelectNodes("//*[local-name()='Project']") | ForEach-Object {
        [string]$_.Path
    })
    $ledger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($project in $projects) {
        Assert-SafeRepositoryPath -Path $project
        if ($ledger.ContainsKey($project)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "solution contains duplicate or case-colliding project '$project'.")
        }
        $ledger[$project] = $project
    }
    return @(Get-OrdinalSortedStrings -Values $projects -Unique)
}

function Get-RevisionContentManifest {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Paths
    )

    $entries = [Collections.Generic.List[object]]::new()
    foreach ($path in @(Get-OrdinalSortedStrings -Values $Paths -Unique)) {
        Assert-SafeRepositoryPath -Path $path
        $state = Get-FileState -Revision $Revision -Path $path
        if ($null -eq $state.Sha256) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "manifest path '$path' is absent at $Revision.")
        }
        $entries.Add([pscustomobject]@{ path = $path; sha256 = [string]$state.Sha256 })
    }
    $material = @($entries | ForEach-Object { "$($_.path):$($_.sha256)" }) -join "`n"
    return [pscustomobject]@{
        Count = $entries.Count
        Sha256 = Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes($material))
    }
}

function Get-RequiredBaselineObject {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][string]$Name
    )

    $property = $Baseline.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or
        $property.Value -is [Array] -or $property.Value -is [string] -or
        $property.Value -is [ValueType]) {
        Stop-MigrationValidation -Code 'COUNTS' -Message "baseline.$Name must be a JSON object."
    }
    return $property.Value
}

function Get-BaselineManifestValue {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$ExpectedProperties
    )

    $manifest = Get-RequiredBaselineObject -Baseline $Baseline -Name $Name
    $actualProperties = @($manifest.PSObject.Properties.Name)
    foreach ($propertyName in $ExpectedProperties) {
        if ($actualProperties -cnotcontains $propertyName) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline.$Name is missing '$propertyName'.")
        }
    }
    foreach ($countName in @($ExpectedProperties | Where-Object {
        $_ -in @('count', 'runnerCases')
    })) {
        $value = $manifest.$countName
        if (($value -isnot [long] -and $value -isnot [int]) -or [long]$value -lt 0) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline.$Name.$countName must be a non-negative integer.")
        }
    }
    foreach ($hashName in @($ExpectedProperties | Where-Object {
        $_ -in @('sha256', 'sourceSha256')
    })) {
        if ($manifest.$hashName -isnot [string] -or
            [string]$manifest.$hashName -cnotmatch '^[0-9a-f]{64}$') {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline.$Name.$hashName must be one lowercase SHA-256.")
        }
    }
    return $manifest
}

function Get-BaselineState {
    param([Parameter(Mandatory)][string]$Revision)

    $baselineBytes = Get-GitBlobBytes -Revision $Revision -Path $script:BaselinePath
    try {
        $baselineJson = [Text.UTF8Encoding]::new($false, $true).GetString($baselineBytes)
        $baseline = ConvertFrom-StrictJson -Json $baselineJson -Location "$($script:BaselinePath)@$Revision"
    }
    catch {
        Stop-MigrationValidation -Code 'COUNTS' -Message "could not parse baseline at ${Revision}: $($_.Exception.Message)"
    }

    if (@($baseline.PSObject.Properties.Name) -cnotcontains 'projects' -or
        $baseline.projects -isnot [Array]) {
        Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects must be a JSON array.'
    }
    if (@($baseline.PSObject.Properties.Name) -cnotcontains 'protectedAssets' -or
        $baseline.protectedAssets -isnot [Array]) {
        Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.protectedAssets must be a JSON array.'
    }

    $workflowManifest = Get-BaselineManifestValue -Baseline $baseline -Name 'workflowManifest' -ExpectedProperties @('count', 'sha256')
    $projectManifest = Get-BaselineManifestValue -Baseline $baseline -Name 'projectManifest' -ExpectedProperties @('count', 'sha256')
    $buildControlManifest = Get-BaselineManifestValue -Baseline $baseline -Name 'buildControlManifest' -ExpectedProperties @('count', 'sha256')
    $restoreControlManifest = Get-BaselineManifestValue -Baseline $baseline -Name 'restoreControlManifest' -ExpectedProperties @('count', 'sha256')
    $frontendTestManifest = Get-BaselineManifestValue -Baseline $baseline -Name 'frontendTestManifest' -ExpectedProperties @('count', 'sha256', 'runnerCases')
    $deploymentBehavior = Get-BaselineManifestValue -Baseline $baseline -Name 'deploymentBehavior' -ExpectedProperties @('sourceSha256', 'runnerCases')

    $revisionPaths = @(Get-RevisionPaths -Revision $Revision)
    $workflowPaths = @($revisionPaths | Where-Object {
        $_.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
        [IO.Path]::GetExtension($_) -in @('.yml', '.yaml')
    })
    $projectPaths = @($revisionPaths | Where-Object {
        [IO.Path]::GetExtension($_) -ieq '.csproj'
    })
    $buildControlPaths = @($revisionPaths | Where-Object {
        Test-IsBuildControlPath -Path $_
    })
    $restoreControlPaths = @($revisionPaths | Where-Object {
        Test-IsRestoreControlPath -Path $_
    })
    $frontendTestPaths = @($revisionPaths | Where-Object {
        Test-IsFrontendTestPath -Path $_
    })
    $actualWorkflowManifest = Get-RevisionContentManifest -Revision $Revision -Paths $workflowPaths
    $actualProjectManifest = Get-RevisionContentManifest -Revision $Revision -Paths $projectPaths
    $actualBuildControlManifest = Get-RevisionContentManifest -Revision $Revision -Paths $buildControlPaths
    $actualRestoreControlManifest = Get-RevisionContentManifest -Revision $Revision -Paths $restoreControlPaths
    $actualFrontendTestManifest = Get-RevisionContentManifest -Revision $Revision -Paths $frontendTestPaths
    $deploymentPath = 'deploy/tests/deployment-behavior.sh'
    $deploymentState = Get-FileState -Revision $Revision -Path $deploymentPath
    if ($null -eq $deploymentState.Sha256) {
        Stop-MigrationValidation -Code 'COUNTS' -Message (
            'deploymentBehavior source is absent from the described revision.')
    }
    $deploymentText = Get-StrictUtf8GitBlobText -Revision $Revision -Path $deploymentPath
    $deploymentSourceDeclarations = [regex]::Matches(
        $deploymentText,
        '(?m)^[ \t]*pass(?:[ \t]+|$)').Count

    $protectedAssets = @($baseline.protectedAssets)
    $protectedLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $protectedMaterialLines = [Collections.Generic.List[string]]::new()
    foreach ($asset in $protectedAssets) {
        if ($null -eq $asset -or $asset -is [Array] -or $asset -is [string] -or
            $asset -is [ValueType] -or
            @($asset.PSObject.Properties.Name) -cnotcontains 'path' -or
            @($asset.PSObject.Properties.Name) -cnotcontains 'sha256' -or
            $asset.path -isnot [string] -or $asset.sha256 -isnot [string]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                'baseline.protectedAssets[] must contain string path and sha256.')
        }
        $assetPath = [string]$asset.path
        $assetHash = [string]$asset.sha256
        Assert-SafeRepositoryPath -Path $assetPath
        if ($assetHash -cnotmatch '^[0-9a-f]{64}$' -or
            $protectedLedger.ContainsKey($assetPath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline protected asset '$assetPath' has an invalid hash or duplicate/case collision.")
        }
        $protectedLedger[$assetPath] = $assetPath
        $actualAsset = Get-FileState -Revision $Revision -Path $assetPath
        if ($null -eq $actualAsset.Sha256) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "protected asset '$assetPath' is absent from the described revision.")
        }
        $protectedMaterialLines.Add("${assetPath}:$($actualAsset.Sha256)")
    }
    $protectedMaterialLines.Sort([StringComparer]::Ordinal)
    $protectedAssetsSha256 = Get-Sha256Hex -Bytes (
        [Text.Encoding]::UTF8.GetBytes(($protectedMaterialLines -join "`n")))

    $projects = @($baseline.projects)
    $projectPathLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $frozenSourceLedger = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $declarations = [long]0
    $executionTemplates = [long]0
    $projectedCases = [long]0
    $runnerCases = [long]0
    foreach ($project in $projects) {
        if ($null -eq $project -or $project -is [Array] -or $project -is [string] -or
            $project -is [ValueType]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message 'baseline.projects[] must be a JSON object.'
        }
        $requiredNames = @(
            'projectPath', 'baselineDeclarations', 'baselineExecutionTemplates',
            'baselineProjectedCases', 'baselineRunnerCases', 'tests', 'runnerCases',
            'frozenSourceFiles', 'frozenSourceHashes')
        $actualNames = @($project.PSObject.Properties.Name)
        foreach ($requiredName in $requiredNames) {
            if ($actualNames -cnotcontains $requiredName) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline project is missing '$requiredName'.")
            }
        }
        if ($project.projectPath -isnot [string] -or
            $project.tests -isnot [Array] -or
            $project.runnerCases -isnot [Array] -or
            $project.frozenSourceFiles -isnot [Array] -or
            $null -eq $project.frozenSourceHashes -or
            $project.frozenSourceHashes -is [Array] -or
            $project.frozenSourceHashes -is [string] -or
            $project.frozenSourceHashes -is [ValueType]) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                'baseline projectPath/tests/runnerCases/frozenSourceFiles/frozenSourceHashes have invalid JSON types.')
        }
        $projectPath = [string]$project.projectPath
        Assert-SafeRepositoryPath -Path $projectPath
        if ($projectPathLedger.ContainsKey($projectPath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline contains duplicate or case-colliding project '$projectPath'.")
        }
        $projectPathLedger[$projectPath] = $projectPath
        foreach ($countName in $requiredNames[1..4]) {
            $countValue = $project.$countName
            if (($countValue -isnot [long] -and $countValue -isnot [int]) -or
                [long]$countValue -lt 0) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline $projectPath.$countName must be a non-negative integer.")
            }
        }

        $projectDeclarations = @($project.tests).Count
        $projectExecutionTemplates = [long]0
        $projectProjectedCases = [long]0
        foreach ($test in @($project.tests)) {
            if ($null -eq $test -or $test -is [Array] -or $test -is [string] -or
                $test -is [ValueType] -or
                @($test.PSObject.Properties.Name) -cnotcontains 'executionTypes' -or
                @($test.PSObject.Properties.Name) -cnotcontains 'projectedCases' -or
                $test.executionTypes -isnot [Array] -or
                (($test.projectedCases -isnot [long]) -and
                    ($test.projectedCases -isnot [int])) -or
                [long]$test.projectedCases -lt 0) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline $projectPath contains an invalid test execution record.")
            }
            $projectExecutionTemplates += @($test.executionTypes).Count
            $projectProjectedCases += [long]$test.projectedCases
        }
        if ($projectDeclarations -ne [long]$project.baselineDeclarations -or
            $projectExecutionTemplates -ne [long]$project.baselineExecutionTemplates -or
            $projectProjectedCases -ne [long]$project.baselineProjectedCases -or
            @($project.runnerCases).Count -ne [long]$project.baselineRunnerCases) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline $projectPath summary differs from its exact declaration/execution/runner rosters.")
        }
        foreach ($sourcePath in @($project.frozenSourceFiles)) {
            if ($sourcePath -isnot [string]) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline $projectPath frozenSourceFiles must contain strings.")
            }
            Assert-SafeRepositoryPath -Path ([string]$sourcePath)
            if ($frozenSourceLedger.ContainsKey([string]$sourcePath)) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline has duplicate or case-colliding frozen source '$sourcePath'.")
            }
            $frozenSourceLedger[[string]$sourcePath] = [string]$sourcePath
            $hashProperty = $project.frozenSourceHashes.PSObject.Properties[[string]$sourcePath]
            if ($null -eq $hashProperty -or $hashProperty.Value -isnot [string] -or
                [string]$hashProperty.Value -cnotmatch '^[0-9a-f]{64}$') {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "baseline frozen source '$sourcePath' is missing one lowercase frozenSourceHashes entry.")
            }
            $actualFrozenSource = Get-FileState -Revision $Revision -Path ([string]$sourcePath)
            if ($null -eq $actualFrozenSource.Sha256 -or
                [string]$actualFrozenSource.Sha256 -cne [string]$hashProperty.Value) {
                Stop-MigrationValidation -Code 'COUNTS' -Message (
                    "frozen source '$sourcePath' is absent or differs from its baseline hash.")
            }
        }
        $frozenHashNames = @($project.frozenSourceHashes.PSObject.Properties |
            ForEach-Object { [string]$_.Name })
        if ($frozenHashNames.Count -ne @($project.frozenSourceFiles).Count -or
            @($frozenHashNames | Where-Object {
                [string]$_ -cnotin @($project.frozenSourceFiles)
            }).Count -ne 0) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline $projectPath frozenSourceHashes must exactly match frozenSourceFiles.")
        }

        $declarations += $projectDeclarations
        $executionTemplates += $projectExecutionTemplates
        $projectedCases += $projectProjectedCases
        $runnerCases += @($project.runnerCases).Count
    }

    $solutionProjects = @(Get-SolutionProjectPaths -Revision $Revision)
    $testProjects = @(Get-OrdinalSortedStrings -Values @(
        $projects | ForEach-Object { [string]$_.projectPath }) -Unique)
    if ($solutionProjects.Count -ne [long]$actualProjectManifest.Count) {
        Stop-MigrationValidation -Code 'COUNTS' -Message (
            'solution project roster and actual project manifest count differ.')
    }
    foreach ($testProject in $testProjects) {
        if ($testProject -cnotin $solutionProjects) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "baseline test project '$testProject' is absent from IIoT.CloudPlatform.slnx.")
        }
    }

    $counts = [ordered]@{
        repositoryProjects = $solutionProjects.Count
        testProjects = $testProjects.Count
        testSourceFiles = @($revisionPaths | Where-Object {
            $_.StartsWith('src/tests/', [StringComparison]::Ordinal) -and
            $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
        }).Count
        frozenSourceFiles = $frozenSourceLedger.Count
        declarations = $declarations
        executionTemplates = $executionTemplates
        projectedCases = $projectedCases
        runnerCases = $runnerCases
        protectedAssets = $protectedAssets.Count
        workflowFiles = [long]$actualWorkflowManifest.Count
        projectFiles = [long]$actualProjectManifest.Count
        buildControlFiles = [long]$actualBuildControlManifest.Count
        restoreControlFiles = [long]$actualRestoreControlManifest.Count
        frontendTestFiles = [long]$actualFrontendTestManifest.Count
        frontendRunnerCases = [long]$frontendTestManifest.runnerCases
        deploymentSourceDeclarations = $deploymentSourceDeclarations
        deploymentRunnerCases = [long]$deploymentBehavior.runnerCases
    }

    return [pscustomobject]@{
        State = [pscustomobject][ordered]@{
            baselineSha256 = Get-Sha256Hex -Bytes $baselineBytes
            protectedManifestSha256 = Get-ProtectedManifestDigest -Revision $Revision
            manifests = [pscustomobject][ordered]@{
                protectedAssetsSha256 = $protectedAssetsSha256
                workflowSha256 = [string]$actualWorkflowManifest.Sha256
                projectSha256 = [string]$actualProjectManifest.Sha256
                buildControlSha256 = [string]$actualBuildControlManifest.Sha256
                restoreControlSha256 = [string]$actualRestoreControlManifest.Sha256
                frontendTestSha256 = [string]$actualFrontendTestManifest.Sha256
                deploymentSourceSha256 = [string]$deploymentState.Sha256
            }
            counts = [pscustomobject]$counts
        }
        RepositoryProjects = $solutionProjects
        TestProjects = $testProjects
        FrozenSources = @(Get-OrdinalSortedStrings -Values @($frozenSourceLedger.Values) -Unique)
    }
}

function Get-ProjectChanges {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    $sourceProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.RepositoryProjects), [StringComparer]::Ordinal)
    $targetProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.RepositoryProjects), [StringComparer]::Ordinal)
    $sourceTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Source.TestProjects), [StringComparer]::Ordinal)
    $targetTests = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.TestProjects), [StringComparer]::Ordinal)
    return [pscustomobject][ordered]@{
        added = @(Get-OrdinalSortedStrings -Values @(
            $Target.RepositoryProjects | Where-Object { -not $sourceProjects.Contains($_) }))
        removed = @(Get-OrdinalSortedStrings -Values @(
            $Source.RepositoryProjects | Where-Object { -not $targetProjects.Contains($_) }))
        addedTests = @(Get-OrdinalSortedStrings -Values @(
            $Target.TestProjects | Where-Object { -not $sourceTests.Contains($_) }))
        removedTests = @(Get-OrdinalSortedStrings -Values @(
            $Source.TestProjects | Where-Object { -not $targetTests.Contains($_) }))
    }
}

function Assert-FrozenSourcesDoNotDecrease {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    $targetFrozen = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Target.FrozenSources), [StringComparer]::Ordinal)
    foreach ($sourcePath in @($Source.FrozenSources)) {
        if (-not $targetFrozen.Contains([string]$sourcePath)) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "v1 migration cannot remove frozen source '$sourcePath' from the reviewed roster.")
        }
    }
}

function Assert-NoDuplicateJsonKeys {
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate JSON key '$($property.Name)' at $Location."
            }
            Assert-NoDuplicateJsonKeys -Element $property.Value -Location "$Location.$($property.Name)"
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateJsonKeys -Element $item -Location "$Location[$index]"
            $index++
        }
    }
}

function ConvertFrom-StrictJson {
    param(
        [Parameter(Mandatory)][string]$Json,
        [Parameter(Mandatory)][string]$Location
    )

    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($Json)
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "JSON root at $Location must be an object."
        }
        Assert-NoDuplicateJsonKeys -Element $document.RootElement -Location $Location
        return ConvertFrom-JsonElement -Element $document.RootElement
    }
    catch {
        if ($_.Exception.Message.StartsWith($script:RuleId, [StringComparison]::Ordinal)) { throw }
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid JSON at ${Location}: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function ConvertFrom-JsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-JsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-JsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            $decimal = [decimal]0
            if ($Element.TryGetDecimal([ref]$decimal)) { return $decimal }
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON number '$($Element.GetRawText())'."
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)][object]$Object,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Location
    )

    if ($null -eq $Object -or $Object -is [Array] -or $Object -is [string] -or
        $Object -is [ValueType]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON object."
    }
    $actual = @($Object.PSObject.Properties.Name)
    $expectedSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Expected, [StringComparer]::Ordinal)
    $actualSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$actual, [StringComparer]::Ordinal)
    $missing = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $unknown = @($actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($missing.Count -gt 0 -or $unknown.Count -gt 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "$Location properties mismatch; missing=[$($missing -join ',')] unknown=[$($unknown -join ',')].")
    }
}

function Assert-JsonString {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [string]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON string."
    }
}

function Assert-JsonArray {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    if ($Value -isnot [Array]) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be a JSON array."
    }
}

function Assert-HashOrNull {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][bool]$MustExist,
        [Parameter(Mandatory)][string]$Location
    )

    if ($MustExist) {
        Assert-JsonString -Value $Value -Location $Location
        if ([string]$Value -cnotmatch '^[0-9a-f]{64}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be one lowercase SHA-256."
        }
    }
    elseif ($null -ne $Value) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be null."
    }
}

function Assert-SortedUniqueStrings {
    param(
        [AllowNull()][object]$Values,
        [Parameter(Mandatory)][string]$Location,
        [switch]$Paths
    )

    Assert-JsonArray -Value $Values -Location $Location
    $items = @($Values)
    foreach ($item in $items) { Assert-JsonString -Value $item -Location "$Location[]" }
    if ($Paths) {
        $caseLedger = [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($item in $items) {
            $path = [string]$item
            if ($caseLedger.ContainsKey($path)) {
                Stop-MigrationValidation -Code 'RECEIPT' -Message (
                    "$Location contains duplicate or case-colliding path '$path'.")
            }
            $caseLedger[$path] = $path
        }
    }
    $expected = @(Get-OrdinalSortedStrings -Values @(
        $items | ForEach-Object { [string]$_ }) -Unique)
    if ($items.Count -ne $expected.Count) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location contains duplicates."
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
        if ([string]$items[$index] -cne $expected[$index]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must be ordinal-sorted and unique."
        }
        if ($Paths) { Assert-SafeRepositoryPath -Path ([string]$items[$index]) }
    }
}

function ConvertTo-UtcTimestamp {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Location
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        $Value,
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed)) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location must use UTC yyyy-MM-ddTHH:mm:ssZ."
    }
    return $parsed.ToUniversalTime()
}

function Assert-StateShape {
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][string]$Location
    )

    Assert-ExactProperties -Object $State -Expected @(
        'baselineSha256', 'protectedManifestSha256', 'manifests', 'counts') -Location $Location
    Assert-HashOrNull -Value $State.baselineSha256 -MustExist $true -Location "$Location.baselineSha256"
    Assert-HashOrNull -Value $State.protectedManifestSha256 -MustExist $true -Location "$Location.protectedManifestSha256"
    Assert-ExactProperties -Object $State.manifests -Expected @(
        'protectedAssetsSha256', 'workflowSha256', 'projectSha256',
        'buildControlSha256', 'restoreControlSha256', 'frontendTestSha256',
        'deploymentSourceSha256') -Location "$Location.manifests"
    foreach ($name in @(
        'protectedAssetsSha256', 'workflowSha256', 'projectSha256',
        'buildControlSha256', 'restoreControlSha256', 'frontendTestSha256',
        'deploymentSourceSha256')) {
        Assert-HashOrNull -Value $State.manifests.$name -MustExist $true -Location "$Location.manifests.$name"
    }
    Assert-ExactProperties `
        -Object $State.counts `
        -Expected $script:ReceiptCountFields `
        -Location "$Location.counts"
    foreach ($name in $script:ReceiptCountFields) {
        $value = $State.counts.$name
        if ($value -isnot [long] -and $value -isnot [int]) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name must be an integer."
        }
        if ([long]$value -lt 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "$Location.counts.$name cannot be negative."
        }
    }
}

function Assert-TestEvidenceDoesNotDecrease {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    foreach ($name in $script:ReceiptCountFields) {
        if ([long]$Target.counts.$name -lt [long]$Source.counts.$name) {
            Stop-MigrationValidation -Code 'COUNTS' -Message (
                "target $name cannot decrease during a v1 baseline migration; " +
                "source=$($Source.counts.$name) target=$($Target.counts.$name).")
        }
    }
}

function Assert-ReceiptShape {
    param(
        [Parameter(Mandatory)][object]$Receipt,
        [Parameter(Mandatory)][string]$ExpectedPath,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    Assert-ExactProperties -Object $Receipt -Expected @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
        'ruleIds', 'source', 'target', 'projectChanges', 'changes') -Location 'receipt'
    foreach ($name in @(
        'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
        'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason')) {
        Assert-JsonString -Value $Receipt.$name -Location "receipt.$name"
    }
    if ([string]$Receipt.schemaVersion -cne $script:ReceiptSchemaVersion -or
        [string]$Receipt.ruleId -cne $script:RuleId) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'unsupported schemaVersion or ruleId.'
    }
    if ([string]$Receipt.migrationId -cnotmatch '^CLOUD-BASELINE-MIG-[A-Z0-9][A-Z0-9-]{2,80}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid migrationId '$($Receipt.migrationId)'."
    }
    $expectedPendingPath = "$($script:PendingRoot)$($Receipt.migrationId).json"
    if ($ExpectedPath -cne $expectedPendingPath) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "receipt path '$ExpectedPath' must equal '$expectedPendingPath'.")
    }
    if ([string]$Receipt.issuedAgainstRevision -cnotmatch '^[0-9a-f]{40}$') {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'issuedAgainstRevision must be one full lowercase commit SHA.'
    }
    if ([string]$Receipt.owner -cnotin $script:ApprovedOwners -or
        [string]$Receipt.approvedBy -cnotin $script:ApprovedApprovers) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'owner or approvedBy is not in the reviewed registry.'
    }
    $reasonText = [string]$Receipt.reason
    if ($reasonText -cne $reasonText.Trim() -or $reasonText.Length -lt 20 -or $reasonText.Length -gt 500) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'reason must be trimmed and contain 20-500 characters.'
    }

    $issued = ConvertTo-UtcTimestamp -Value ([string]$Receipt.issuedAtUtc) -Location 'receipt.issuedAtUtc'
    $expires = ConvertTo-UtcTimestamp -Value ([string]$Receipt.expiresAtUtc) -Location 'receipt.expiresAtUtc'
    if ($expires -le $issued -or $expires - $issued -gt $script:MaximumReceiptLifetime) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt lifetime must be positive and no longer than 7 days.'
    }
    if (-not $AllowExpired -and ($issued -gt $Now.AddMinutes(5) -or $expires -lt $Now)) {
        Stop-MigrationValidation -Code 'EXPIRY' -Message 'receipt is not currently valid.'
    }

    Assert-JsonArray -Value $Receipt.ruleIds -Location 'receipt.ruleIds'
    $ruleIdsValue = @($Receipt.ruleIds)
    if ($ruleIdsValue.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'ruleIds cannot be empty.'
    }
    Assert-SortedUniqueStrings -Values $ruleIdsValue -Location 'receipt.ruleIds'
    foreach ($receiptRuleId in $ruleIdsValue) {
        if ([string]$receiptRuleId -cnotmatch '^[A-Z][A-Z0-9-]{2,80}$') {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid governed rule ID '$receiptRuleId'."
        }
        if ([string]$receiptRuleId -cnotin $script:ApprovedGovernedRuleIds) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                "governed rule ID '$receiptRuleId' is not in the reviewed registry.")
        }
    }
    $hasTrustUpgradeRule = $ruleIdsValue -ccontains $script:TrustUpgradeRuleId
    if ($hasTrustUpgradeRule -and
        ($ruleIdsValue.Count -ne 1 -or
         [string]$ruleIdsValue[0] -cne $script:TrustUpgradeRuleId)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'CLOUD-BASELINE-TRUST-UPGRADE-001 must be the singleton governed Rule ID.')
    }

    Assert-StateShape -State $Receipt.source -Location 'receipt.source'
    Assert-StateShape -State $Receipt.target -Location 'receipt.target'
    Assert-TestEvidenceDoesNotDecrease -Source $Receipt.source -Target $Receipt.target
    Assert-ExactProperties -Object $Receipt.projectChanges -Expected @(
        'added', 'removed', 'addedTests', 'removedTests') -Location 'receipt.projectChanges'
    foreach ($name in @('added', 'removed', 'addedTests', 'removedTests')) {
        Assert-SortedUniqueStrings -Values $Receipt.projectChanges.$name -Location "receipt.projectChanges.$name" -Paths
    }
    $addedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.added), [StringComparer]::OrdinalIgnoreCase)
    $removedProjects = [Collections.Generic.HashSet[string]]::new(
        [string[]]@($Receipt.projectChanges.removed), [StringComparer]::OrdinalIgnoreCase)
    if (@($addedProjects | Where-Object { $removedProjects.Contains($_) }).Count -ne 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            'projectChanges.added and projectChanges.removed must be disjoint.')
    }
    foreach ($addedTest in @($Receipt.projectChanges.addedTests)) {
        if (-not $addedProjects.Contains([string]$addedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.addedTests must be a subset of projectChanges.added.')
        }
    }
    foreach ($removedTest in @($Receipt.projectChanges.removedTests)) {
        if (-not $removedProjects.Contains([string]$removedTest)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message (
                'projectChanges.removedTests must be a subset of projectChanges.removed.')
        }
    }

    Assert-JsonArray -Value $Receipt.changes -Location 'receipt.changes'
    $changes = @($Receipt.changes)
    if ($changes.Count -eq 0) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes cannot be empty.'
    }
    if ($changes.Count -gt $script:MaximumReceiptChanges) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message (
            "changes exceeds the $($script:MaximumReceiptChanges)-path limit.")
    }
    $changePaths = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $hasProtectedChange = $false
    $hasValidatorUpgrade = $false
    $hasOrdinaryChange = $false
    $lastPath = $null
    foreach ($change in $changes) {
        Assert-ExactProperties -Object $change -Expected @(
            'path', 'status', 'beforeMode', 'beforeSha256', 'afterMode', 'afterSha256') -Location 'receipt.changes[]'
        Assert-JsonString -Value $change.path -Location 'receipt.changes[].path'
        Assert-JsonString -Value $change.status -Location "receipt.changes[$($change.path)].status"
        $path = [string]$change.path
        Assert-SafeRepositoryPath -Path $path
        if ($null -ne $lastPath -and [StringComparer]::Ordinal.Compare($lastPath, $path) -ge 0) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'changes must be ordinal-sorted by unique path.'
        }
        $lastPath = $path
        if ($changePaths.ContainsKey($path)) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "duplicate or case-colliding change path '$path'."
        }
        $changePaths[$path] = $path
        if (Test-IsReceiptStatePath -Path $path) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message 'pending/consumed receipt moves are implicit and cannot appear in changes.'
        }
        if (Test-IsImmutableTrustProofPath -Path $path) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 proof asset '$path' is immutable and cannot be changed by a receipt.")
        }
        if (Test-IsTrustUpgradePath -Path $path) {
            $hasValidatorUpgrade = $true
        } else {
            $hasOrdinaryChange = $true
        }
        if (Test-IsProtectedPath -Path $path) { $hasProtectedChange = $true }

        $status = [string]$change.status
        if ($status -cnotin @('A', 'M', 'D')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid change status '$status' for '$path'."
        }
        $beforeExists = $status -cin @('M', 'D')
        $afterExists = $status -cin @('A', 'M')
        if ((Test-IsTrustUpgradePath -Path $path) -and
            ($status -cne 'M' -or
             [string]$change.beforeMode -cne '100644' -or
             [string]$change.afterMode -cne '100644')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 trust upgrade may only modify the existing mode-100644 validator '$path'.")
        }
        if ($beforeExists) { Assert-JsonString -Value $change.beforeMode -Location "change[$path].beforeMode" }
        if ($afterExists) { Assert-JsonString -Value $change.afterMode -Location "change[$path].afterMode" }
        if ($beforeExists -and [string]$change.beforeMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid beforeMode for '$path'."
        }
        if (-not $beforeExists -and $null -ne $change.beforeMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "beforeMode for added '$path' must be null."
        }
        if ($afterExists -and [string]$change.afterMode -cnotin @('100644', '100755')) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "invalid afterMode for '$path'."
        }
        if (-not $afterExists -and $null -ne $change.afterMode) {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "afterMode for deleted '$path' must be null."
        }
        Assert-HashOrNull -Value $change.beforeSha256 -MustExist $beforeExists -Location "change[$path].beforeSha256"
        Assert-HashOrNull -Value $change.afterSha256 -MustExist $afterExists -Location "change[$path].afterSha256"
    }
    if (-not $hasProtectedChange) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message 'a migration receipt must govern at least one protected asset change.'
    }
    $changesBaseline = $changePaths.ContainsKey($script:BaselinePath)
    $changesPolicy = $changePaths.ContainsKey($script:PolicyPath)
    if ($changesBaseline -and $changesPolicy) {
        Stop-MigrationValidation -Code 'POLICY' -Message (
            'one receipt cannot change cloud-test-governance.baseline.json and TestCloudTestGovernancePolicy.ps1 together.')
    }
    $isTrustUpgrade = $hasTrustUpgradeRule
    if ($hasValidatorUpgrade -and (-not $isTrustUpgrade -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'validator changes require one isolated CLOUD-BASELINE-TRUST-UPGRADE-001 receipt.')
    }
    if ($isTrustUpgrade -and (-not $hasValidatorUpgrade -or $hasOrdinaryChange)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'CLOUD-BASELINE-TRUST-UPGRADE-001 may modify only the existing mode-100644 validator.')
    }
}

function Assert-ObjectJsonEqual {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory)][string]$Location
    )

    $expectedJson = ConvertTo-CanonicalJson -Value $Expected
    $actualJson = ConvertTo-CanonicalJson -Value $Actual
    if ($expectedJson -cne $actualJson) {
        Stop-MigrationValidation -Code 'MISMATCH' -Message (
            "$Location differs from the receipt. expected=$expectedJson actual=$actualJson")
    }
}

function ConvertTo-CanonicalJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $properties = [Collections.Generic.List[object]]::new()
            foreach ($property in $Element.EnumerateObject()) {
                $properties.Add([pscustomobject]@{
                    Name = $property.Name
                    Value = $property.Value.Clone()
                })
            }
            $comparer = [Collections.Generic.Comparer[object]]::Create(
                [Comparison[object]]{
                    param($left, $right)
                    return [StringComparer]::Ordinal.Compare([string]$left.Name, [string]$right.Name)
                })
            $properties.Sort($comparer)
            $members = @($properties | ForEach-Object {
                $nameJson = [Text.Json.JsonSerializer]::Serialize(
                    [string]$_.Name,
                    $script:JsonSerializerOptions)
                "$nameJson`:$(ConvertTo-CanonicalJsonElement -Element $_.Value)"
            })
            return "{$($members -join ',')}"
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = @($Element.EnumerateArray() | ForEach-Object {
                ConvertTo-CanonicalJsonElement -Element $_
            })
            return "[$($items -join ',')]"
        }
        ([Text.Json.JsonValueKind]::String) {
            return [Text.Json.JsonSerializer]::Serialize(
                $Element.GetString(),
                $script:JsonSerializerOptions)
        }
        ([Text.Json.JsonValueKind]::Number) { return $Element.GetRawText() }
        ([Text.Json.JsonValueKind]::True) { return 'true' }
        ([Text.Json.JsonValueKind]::False) { return 'false' }
        ([Text.Json.JsonValueKind]::Null) { return 'null' }
        default {
            Stop-MigrationValidation -Code 'RECEIPT' -Message "unsupported JSON value kind '$($Element.ValueKind)'."
        }
    }
}

function ConvertTo-CanonicalJson {
    param([AllowNull()][object]$Value)

    $json = ConvertTo-Json -InputObject $Value -Depth 100 -Compress
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        return ConvertTo-CanonicalJsonElement -Element $document.RootElement
    }
    finally {
        $document.Dispose()
    }
}

function Get-ReceiptAtRevision {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [switch]$AllowExpired
    )

    $entry = Get-GitEntry -Revision $Revision -Path $Path
    $sizeText = (Invoke-GitText -Arguments @('cat-file', '-s', $entry.ObjectId)).Text.Trim()
    $blobSize = [long]0
    if (-not [long]::TryParse($sizeText, [ref]$blobSize) -or $blobSize -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt exceeds $($script:MaximumReceiptBytes) bytes: $Path."
    }
    $bytes = (Invoke-GitBytes -Arguments @('cat-file', 'blob', $entry.ObjectId)).Bytes
    try {
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch {
        Stop-MigrationValidation -Code 'RECEIPT' -Message "receipt is not valid UTF-8: $Path."
    }
    $receipt = ConvertFrom-StrictJson -Json $json -Location $Path
    Assert-ReceiptShape -Receipt $receipt -ExpectedPath $Path -Now $Now -AllowExpired:$AllowExpired
    return [pscustomobject]@{ Receipt = $receipt; Bytes = $bytes }
}

function Get-PendingReceiptPaths {
    param([Parameter(Mandatory)][string]$Revision)

    $paths = @((Get-RevisionPaths -Revision $Revision) | Where-Object {
        $_.StartsWith($script:PendingRoot, [StringComparison]::Ordinal) -and
        $_.EndsWith('.json', [StringComparison]::Ordinal)
    })
    return @(Get-OrdinalSortedStrings -Values $paths)
}

function Assert-MigrationIdNotFinalized {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$MigrationId
    )

    $consumedPath = "$($script:ConsumedRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $consumedPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already consumed."
    }
    $cancelledPath = "$($script:CancelledRoot)$MigrationId.json"
    if ($null -ne (Get-GitEntry -Revision $Revision -Path $cancelledPath -AllowMissing)) {
        Stop-MigrationValidation -Code 'REPLAY' -Message "migration '$MigrationId' is already cancelled."
    }
}

function Test-AuthorizationOnlyTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    if ($Diff.Count -ne 1 -or [string]$Diff[0].status -cne 'A' -or
        -not ([string]$Diff[0].path).StartsWith($script:PendingRoot, [StringComparison]::Ordinal)) {
        return $false
    }
    if ([string]$Diff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'pending receipt must use git mode 100644.'
    }

    $targetParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $TargetRevision)).Text.Trim().Split(' ')
    if ($targetParents.Count -ne 2 -or $targetParents[1] -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'authorization must be one single-parent commit directly after its issued base.'
    }

    if (@(Get-PendingReceiptPaths -Revision $BaseRevision).Count -ne 0) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'cannot authorize a second pending receipt.'
    }
    $path = [string]$Diff[0].path
    $loaded = Get-ReceiptAtRevision -Revision $TargetRevision -Path $path -Now $Now
    $receipt = $loaded.Receipt
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId ([string]$receipt.migrationId)
    if ([string]$receipt.issuedAgainstRevision -cne $BaseRevision) {
        Stop-MigrationValidation -Code 'AUTHORIZATION' -Message 'issuedAgainstRevision must equal the authorization commit parent.'
    }
    $baseState = Get-BaselineState -Revision $BaseRevision
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $baseState.State -Location 'authorization source state'
    Write-Host "Cloud baseline migration authorization recorded: $($receipt.migrationId)"
    return $true
}

function Test-CancellationTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1 -or $Diff.Count -ne 2) { return $false }
    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CANCEL' `
        -Context 'receipt cancellation'

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now -AllowExpired
    $migrationIdValue = [string]$loaded.Receipt.migrationId
    $cancelledPath = "$($script:CancelledRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue
    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $cancelledAdd = @($Diff | Where-Object { $_.path -ceq $cancelledPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $cancelledAdd.Count -ne 1) { return $false }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$cancelledAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'pending and cancelled receipts must use git mode 100644.'
    }
    $cancelledBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $cancelledPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $cancelledBytes)) {
        Stop-MigrationValidation -Code 'CANCEL' -Message 'cancelled receipt blob differs from pending receipt.'
    }

    Write-Host "Cloud baseline migration receipt cancelled: $migrationIdValue"
    return $true
}

function Assert-ConsumptionTransition {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Diff,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Now
    )

    Assert-DirectSingleParentTransition `
        -BaseRevision $BaseRevision `
        -TargetRevision $TargetRevision `
        -Code 'CONSUME' `
        -Context 'receipt consumption'

    $pendingPaths = @(Get-PendingReceiptPaths -Revision $BaseRevision)
    if ($pendingPaths.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message "trusted base must contain exactly one pending receipt; found $($pendingPaths.Count)."
    }

    $pendingPath = $pendingPaths[0]
    $loaded = Get-ReceiptAtRevision -Revision $BaseRevision -Path $pendingPath -Now $Now
    $receipt = $loaded.Receipt
    $migrationIdValue = [string]$receipt.migrationId
    $consumedPath = "$($script:ConsumedRoot)$migrationIdValue.json"
    Assert-MigrationIdNotFinalized -Revision $BaseRevision -MigrationId $migrationIdValue

    $baseParents = (Invoke-GitText -Arguments @('rev-list', '--parents', '-n', '1', $BaseRevision)).Text.Trim().Split(' ')
    if ($baseParents.Count -ne 2 -or $baseParents[1] -cne [string]$receipt.issuedAgainstRevision) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'trusted base must be the isolated authorization commit for this receipt.'
    }
    $authorizationDiff = @(Get-DiffRecords -BaseRevision $baseParents[1] -TargetRevision $BaseRevision)
    if ($authorizationDiff.Count -ne 1 -or
        [string]$authorizationDiff[0].status -cne 'A' -or
        [string]$authorizationDiff[0].path -cne $pendingPath -or
        [string]$authorizationDiff[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'authorization commit must add only the pending receipt.'
    }

    $pendingDelete = @($Diff | Where-Object { $_.path -ceq $pendingPath -and $_.status -ceq 'D' })
    $consumedAdd = @($Diff | Where-Object { $_.path -ceq $consumedPath -and $_.status -ceq 'A' })
    if ($pendingDelete.Count -ne 1 -or $consumedAdd.Count -ne 1) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'candidate must atomically move pending receipt to consumed.'
    }
    if ([string]$pendingDelete[0].beforeMode -cne '100644' -or
        [string]$consumedAdd[0].afterMode -cne '100644') {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'pending and consumed receipt must use git mode 100644.'
    }
    $consumedBytes = Get-GitBlobBytes -Revision $TargetRevision -Path $consumedPath
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -cne (Get-Sha256Hex -Bytes $consumedBytes)) {
        Stop-MigrationValidation -Code 'CONSUME' -Message 'consumed receipt blob differs from pending receipt.'
    }

    $actualChanges = @($Diff | Where-Object {
        $_.path -cne $pendingPath -and $_.path -cne $consumedPath
    })
    Assert-ObjectJsonEqual -Expected @($receipt.changes) -Actual $actualChanges -Location 'candidate changes'
    Assert-TargetWorkflowTrustClosure -Revision $TargetRevision

    $sourceState = Get-BaselineState -Revision ([string]$receipt.issuedAgainstRevision)
    $targetState = Get-BaselineState -Revision $TargetRevision
    Assert-FrozenSourcesDoNotDecrease -Source $sourceState -Target $targetState
    Assert-ObjectJsonEqual -Expected $receipt.source -Actual $sourceState.State -Location 'source state'
    Assert-ObjectJsonEqual -Expected $receipt.target -Actual $targetState.State -Location 'target state'
    $projectChanges = Get-ProjectChanges -Source $sourceState -Target $targetState
    Assert-ObjectJsonEqual -Expected $receipt.projectChanges -Actual $projectChanges -Location 'project changes'

    Write-Host "Cloud baseline migration receipt consumed: $migrationIdValue"
}

function New-ReceiptDescription {
    param(
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision,
        [Parameter(Mandatory)][DateTimeOffset]$Issued,
        [Parameter(Mandatory)][DateTimeOffset]$Expires
    )

    $describeArguments = [ordered]@{
        MigrationId = $MigrationId
        RuleIdsCsv = $RuleIdsCsv
        Owner = $Owner
        ApprovedBy = $ApprovedBy
        Reason = $Reason
    }
    $missingDescribeArguments = @($script:DescribeRequiredArgumentNames | Where-Object {
        [string]::IsNullOrWhiteSpace([string]$describeArguments[$_])
    })
    if ($missingDescribeArguments.Count -ne 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "Describe requires $($script:DescribeRequiredArgumentNames -join ', '); " +
            "missing=$($missingDescribeArguments -join ',').")
    }
    $describedRuleIds = @($RuleIdsCsv.Split(',', [StringSplitOptions]::None))
    if ($describedRuleIds.Count -eq 0 -or @($describedRuleIds | Where-Object {
        [string]::IsNullOrWhiteSpace($_) -or $_ -cne $_.Trim()
    }).Count -ne 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'RuleIdsCsv must be a comma-separated list without whitespace or empty items.'
    }
    $hasTrustUpgradeRule = $describedRuleIds -ccontains $script:TrustUpgradeRuleId
    if ($hasTrustUpgradeRule -and
        ($describedRuleIds.Count -ne 1 -or
         $describedRuleIds[0] -cne $script:TrustUpgradeRuleId)) {
        Stop-MigrationValidation -Code 'TRUST' -Message (
            'CLOUD-BASELINE-TRUST-UPGRADE-001 must be the singleton governed Rule ID.')
    }
    $normalizedRuleIds = @(Get-OrdinalSortedStrings -Values $describedRuleIds -Unique)
    $isTrustUpgradeRequest = $hasTrustUpgradeRule

    $diff = @(Get-DiffRecords -BaseRevision $BaseRevision -TargetRevision $TargetRevision)
    if ($diff.Count -eq 0 -or @($diff | Where-Object { Test-IsProtectedPath -Path $_.path }).Count -eq 0) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message 'candidate must change at least one protected asset.'
    }
    foreach ($change in $diff) {
        if (Test-IsReceiptStatePath -Path $change.path) {
            Stop-MigrationValidation -Code 'DESCRIBE' -Message "candidate contains forbidden receipt-state change '$($change.path)'."
        }
        if (Test-IsImmutableTrustProofPath -Path $change.path) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                "v1 proof asset '$($change.path)' is immutable and cannot be changed by a receipt.")
        }
        $isTrustPath = Test-IsTrustUpgradePath -Path $change.path
        if ($isTrustPath -ne $isTrustUpgradeRequest) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                'trust upgrades must be isolated from all ordinary migration changes.')
        }
        if ($isTrustPath -and
            ([string]$change.status -cne 'M' -or
             [string]$change.beforeMode -cne '100644' -or
             [string]$change.afterMode -cne '100644')) {
            Stop-MigrationValidation -Code 'TRUST' -Message (
                'v1 trust upgrade may only modify the existing mode-100644 validator.')
        }
    }

    $source = Get-BaselineState -Revision $BaseRevision
    $target = Get-BaselineState -Revision $TargetRevision
    Assert-FrozenSourcesDoNotDecrease -Source $source -Target $target
    return [pscustomobject][ordered]@{
        schemaVersion = $script:ReceiptSchemaVersion
        ruleId = $script:RuleId
        migrationId = $MigrationId
        issuedAgainstRevision = $BaseRevision
        issuedAtUtc = $Issued.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        expiresAtUtc = $Expires.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        owner = $Owner
        approvedBy = $ApprovedBy
        reason = $Reason.Trim()
        ruleIds = $normalizedRuleIds
        source = $source.State
        target = $target.State
        projectChanges = Get-ProjectChanges -Source $source -Target $target
        changes = $diff
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
}
if ($RemainingArguments.Count -ne 0) {
    Stop-MigrationValidation -Code 'PARAMETER' -Message (
        "unexpected positional arguments: $($RemainingArguments -join ', ').")
}
$script:RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot '.git'))) {
    Stop-MigrationValidation -Code 'ROOT' -Message "RepositoryRoot is not a Git worktree: $script:RepositoryRoot."
}

if ($TrustedBaseRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $CandidateRevision -notmatch '^[0-9A-Fa-f]{40}$' -or
    $TrustedBaseRevision -match '^0{40}$' -or
    $CandidateRevision -match '^0{40}$') {
    Stop-MigrationValidation -Code 'REVISION' -Message (
        'TrustedBaseRevision and CandidateRevision must be explicit non-zero full 40-character SHAs.')
}

$trustedBase = Resolve-Commit -Revision $TrustedBaseRevision -Name 'TrustedBaseRevision'
$candidate = Resolve-Commit -Revision $CandidateRevision -Name 'CandidateRevision'
$head = Resolve-Commit -Revision 'HEAD' -Name 'HEAD'
$now = [DateTimeOffset]::UtcNow

if ($Mode -eq 'Describe') {
    if ($AnchorRelationship -cne $script:DescribeAnchorRelationship) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "Describe only supports $($script:DescribeAnchorRelationship).")
    }
    Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'Describe'
    Assert-LinearHistoryRange -Ancestor $trustedBase -Descendant $candidate -Context 'Describe'
    Assert-TrustImplementationAssets -Revision $trustedBase
    Assert-TrustImplementationAssets -Revision $candidate
    Assert-TargetWorkflowTrustClosure -Revision $candidate
    $issued = if ([string]::IsNullOrWhiteSpace($IssuedAtUtc)) {
        $now
    } else {
        ConvertTo-UtcTimestamp -Value $IssuedAtUtc -Location 'IssuedAtUtc'
    }
    $expires = if ([string]::IsNullOrWhiteSpace($ExpiresAtUtc)) {
        $issued.AddDays(7)
    } else {
        ConvertTo-UtcTimestamp -Value $ExpiresAtUtc -Location 'ExpiresAtUtc'
    }
    $description = New-ReceiptDescription -BaseRevision $trustedBase -TargetRevision $candidate -Issued $issued -Expires $expires
    Assert-ReceiptShape `
        -Receipt $description `
        -ExpectedPath "$($script:PendingRoot)$MigrationId.json" `
        -Now $now
    $json = $description | ConvertTo-Json -Depth 100
    if ([Text.Encoding]::UTF8.GetByteCount("$json`n") -gt $script:MaximumReceiptBytes) {
        Stop-MigrationValidation -Code 'DESCRIBE' -Message (
            "generated receipt exceeds the $($script:MaximumReceiptBytes)-byte limit.")
    }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $json
    } else {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        $outputDirectory = Split-Path $resolvedOutput -Parent
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
        }
        [IO.File]::WriteAllText($resolvedOutput, "$json`n", [Text.UTF8Encoding]::new($false))
        Write-Host "Wrote Cloud migration receipt description: $resolvedOutput"
    }
    exit 0
}

if ($candidate -cne $head) {
    Stop-MigrationValidation -Code 'REVISION' -Message 'Validate requires CandidateRevision to equal checked-out HEAD.'
}

if ($AnchorRelationship -eq 'HeadAncestorOfBase') {
    Assert-Ancestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-FirstParentAncestry -Ancestor $candidate -Descendant $trustedBase -Context 'release anchor'
    Assert-LinearHistoryRange `
        -Ancestor $candidate `
        -Descendant $trustedBase `
        -Context 'release anchor' `
        -ValidateAncestorEndpoint
    Assert-TrustImplementationAssets -Revision $trustedBase
    $reverseDiff = @(Get-DiffRecords -BaseRevision $candidate -TargetRevision $trustedBase)
    $protectedReverseDiff = @($reverseDiff | Where-Object { Test-IsProtectedPath -Path $_.path })
    if ($protectedReverseDiff.Count -ne 0) {
        Stop-MigrationValidation -Code 'RELEASE' -Message (
            "release commit differs from trusted main in $($protectedReverseDiff.Count) protected asset(s).")
    }
    Write-Host "Cloud trusted release anchor passed: head=$candidate trusted=$trustedBase"
    exit 0
}

Assert-Ancestry -Ancestor $trustedBase -Descendant $candidate -Context 'candidate validation'
Assert-LinearHistoryRange -Ancestor $trustedBase -Descendant $candidate -Context 'candidate validation'
Assert-TrustImplementationAssets -Revision $trustedBase
Assert-TrustImplementationAssets -Revision $candidate
$diff = @(Get-DiffRecords -BaseRevision $trustedBase -TargetRevision $candidate)
if (Test-AuthorizationOnlyTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
    exit 0
}

$pendingAtBase = @(Get-PendingReceiptPaths -Revision $trustedBase)
if ($pendingAtBase.Count -gt 0) {
    if (Test-CancellationTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now) {
        exit 0
    }
    Assert-ConsumptionTransition -Diff $diff -BaseRevision $trustedBase -TargetRevision $candidate -Now $now
    exit 0
}
$protectedDiff = @($diff | Where-Object { Test-IsProtectedPath -Path $_.path })
if ($protectedDiff.Count -ne 0) {
    Stop-MigrationValidation -Code 'IMMUTABLE' -Message (
        "protected assets changed without one pending receipt: $(@($protectedDiff.path) -join ', ').")
}

Write-Host "Cloud protected baseline transition is immutable: base=$trustedBase candidate=$candidate"
