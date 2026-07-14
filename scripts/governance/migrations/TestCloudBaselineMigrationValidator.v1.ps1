[CmdletBinding()]
param(
    [string]$ValidatorPath = (Join-Path $PSScriptRoot 'ValidateCloudBaselineMigration.v1.ps1'),
    [string]$TrustedWrapperPath = (Join-Path $PSScriptRoot 'InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'),
    [string]$SchemaPath = (Join-Path $PSScriptRoot 'cloud-baseline-migration-receipt.schema.json'),
    [Parameter(Mandatory)][string]$TrustedWorkflowPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ValidatorPath = (Resolve-Path $ValidatorPath).Path
$TrustedWrapperPath = (Resolve-Path $TrustedWrapperPath).Path
$TrustedWrapperRelativePath = 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
$SchemaPath = (Resolve-Path $SchemaPath).Path
$TrustedWorkflowPath = (Resolve-Path $TrustedWorkflowPath).Path
$SelfPath = (Resolve-Path $MyInvocation.MyCommand.Path).Path
$Now = [DateTimeOffset]::UtcNow
$IssuedAtUtc = $Now.AddMinutes(-5).ToString('yyyy-MM-ddTHH:mm:ssZ')
$ExpiresAtUtc = $Now.AddDays(6).ToString('yyyy-MM-ddTHH:mm:ssZ')
$MigrationId = 'CLOUD-BASELINE-MIG-SELFTEST-001'
$ReceiptRelativePath = "scripts/governance/migrations/pending/$MigrationId.json"
$ConsumedRelativePath = "scripts/governance/migrations/consumed/$MigrationId.json"
$CancelledRelativePath = "scripts/governance/migrations/cancelled/$MigrationId.json"
$script:Passed = 0
$script:Failed = 0
$script:ExpectedTestCount = 114
$script:TempRoots = [Collections.Generic.List[string]]::new()
$canonicalWorkflowText = [IO.File]::ReadAllText(
    $TrustedWorkflowPath).
    Replace("`r`n", "`n").Replace("`r", "`n")
$canonicalSuffixMarker = "      - name: Setup .NET`n"
$canonicalSuffixIndex = $canonicalWorkflowText.IndexOf(
    $canonicalSuffixMarker,
    [StringComparison]::Ordinal)
if ($canonicalSuffixIndex -lt 0) {
    throw 'Trusted base workflow fixture is missing the canonical Setup .NET suffix marker.'
}
$canonicalBaseSuffix = $canonicalWorkflowText.Substring($canonicalSuffixIndex).
    TrimEnd("`r", "`n")
$legacyFullEndToEndHeader = @'
  full-end-to-end:
    needs: build-test
'@.TrimEnd("`r", "`n")
$requiredFinalHeader = @'
  required-final:
    needs: [migration-validator-selftest, build-test]
    if: always()
    runs-on: ubuntu-24.04
    timeout-minutes: 1
    steps:
      - name: Enforce required Cloud gate results
        shell: bash
        env:
          MIGRATION_VALIDATOR_SELFTEST_RESULT: ${{ needs.migration-validator-selftest.result }}
          BUILD_TEST_RESULT: ${{ needs.build-test.result }}
        run: |
          test "$MIGRATION_VALIDATOR_SELFTEST_RESULT" = success
          test "$BUILD_TEST_RESULT" = success

  full-end-to-end:
    needs: required-final
'@.TrimEnd("`r", "`n")
$legacyHeaderCount = [regex]::Matches(
    $canonicalBaseSuffix,
    [regex]::Escape($legacyFullEndToEndHeader)).Count
$requiredHeaderCount = [regex]::Matches(
    $canonicalBaseSuffix,
    [regex]::Escape($requiredFinalHeader)).Count
if ($legacyHeaderCount -eq 1 -and $requiredHeaderCount -eq 0) {
    $script:CanonicalWorkflowSuffix = $canonicalBaseSuffix.Replace(
        $legacyFullEndToEndHeader,
        $requiredFinalHeader)
} elseif ($legacyHeaderCount -eq 0 -and $requiredHeaderCount -eq 1) {
    $script:CanonicalWorkflowSuffix = $canonicalBaseSuffix
} else {
    throw (
        'Trusted base workflow fixture must contain exactly one legacy or final ' +
        'full-end-to-end dependency header.')
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $directory = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertFrom-TestJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $value = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                $value[$property.Name] = ConvertFrom-TestJsonElement -Element $property.Value
            }
            return [pscustomobject]$value
        }
        ([Text.Json.JsonValueKind]::Array) {
            $items = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-TestJsonElement -Element $item))
            }
            return ,$items.ToArray()
        }
        ([Text.Json.JsonValueKind]::String) { return $Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $integer = [long]0
            if ($Element.TryGetInt64([ref]$integer)) { return $integer }
            return $Element.GetDecimal()
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default { throw "Unsupported JSON value kind '$($Element.ValueKind)' in self-test." }
    }
}

function ConvertFrom-TestJson {
    param([Parameter(Mandatory)][string]$Json)

    $document = [Text.Json.JsonDocument]::Parse($Json)
    try { return ConvertFrom-TestJsonElement -Element $document.RootElement }
    finally { $document.Dispose() }
}

function Get-RequiredWorkflowContent {
    $prefix = @'
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
    return "$($prefix.TrimEnd("`r", "`n"))`n$script:CanonicalWorkflowSuffix"
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$Capture
    )

    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in ${Root}: $($output -join [Environment]::NewLine)"
    }
    if ($Capture) { return ($output | Out-String).Trim() }
}

function Export-TestGitBlob {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string]$Destination
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-C', $Root, 'cat-file', 'blob', $ObjectId)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Could not start trusted wrapper extraction.' }
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $stream = [IO.File]::Open(
            $Destination,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try { $process.StandardOutput.BaseStream.CopyTo($stream) }
        finally { $stream.Dispose() }
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) {
            throw "Trusted wrapper extraction failed: $errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Commit-All {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Message
    )

    Invoke-Git -Root $Root -Arguments @('add', '--all')
    Invoke-Git -Root $Root -Arguments @('commit', '--quiet', '-m', $Message)
    return Invoke-Git -Root $Root -Arguments @('rev-parse', 'HEAD') -Capture
}

function Get-TestSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TestGitFileState {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Path
    )

    $entry = Invoke-Git `
        -Root $Root `
        -Arguments @('ls-tree', $Revision, '--', $Path) `
        -Capture
    if ([string]::IsNullOrWhiteSpace($entry)) {
        return [pscustomobject]@{ Mode = $null; Sha256 = $null }
    }
    $entryMatch = [regex]::Match(
        $entry,
        '^(?<Mode>[0-9]{6}) blob (?<ObjectId>[0-9a-f]+)\t' +
            [regex]::Escape($Path) + '$')
    if (-not $entryMatch.Success) {
        throw "Could not parse test Git entry for '$Path' at $Revision."
    }
    $temporaryBlob = Join-Path ([IO.Path]::GetTempPath()) (
        "$([Guid]::NewGuid().ToString('N')).cloud-migration-diff-blob")
    try {
        Export-TestGitBlob `
            -Root $Root `
            -ObjectId $entryMatch.Groups['ObjectId'].Value `
            -Destination $temporaryBlob
        return [pscustomobject]@{
            Mode = $entryMatch.Groups['Mode'].Value
            Sha256 = Get-TestSha256 -Path $temporaryBlob
        }
    }
    finally {
        Remove-Item $temporaryBlob -Force -ErrorAction SilentlyContinue
    }
}

function Get-TestGitDiffRecords {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$BaseRevision,
        [Parameter(Mandatory)][string]$TargetRevision
    )

    $diffText = Invoke-Git `
        -Root $Root `
        -Arguments @(
            'diff-tree', '-r', '--no-commit-id', '--name-status', '--no-renames',
            $BaseRevision, $TargetRevision) `
        -Capture
    if ([string]::IsNullOrWhiteSpace($diffText)) { return @() }

    $records = [Collections.Generic.List[object]]::new()
    foreach ($line in @($diffText -split "`r?`n" | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })) {
        $match = [regex]::Match($line, '^(?<Status>[AMD])\t(?<Path>.+)$')
        if (-not $match.Success) {
            throw "Could not parse test Git diff record '$line'."
        }
        $path = $match.Groups['Path'].Value
        $before = Get-TestGitFileState -Root $Root -Revision $BaseRevision -Path $path
        $after = Get-TestGitFileState -Root $Root -Revision $TargetRevision -Path $path
        $records.Add([pscustomobject][ordered]@{
            path = $path
            status = $match.Groups['Status'].Value
            beforeMode = $before.Mode
            beforeSha256 = $before.Sha256
            afterMode = $after.Mode
            afterSha256 = $after.Sha256
        })
    }
    return @($records.ToArray() | Sort-Object -Property path -CaseSensitive)
}

function Get-TestManifest {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Paths
    )

    $entries = @($Paths | Sort-Object -Unique | ForEach-Object {
        [pscustomobject]@{
            path = [string]$_
            sha256 = Get-TestSha256 -Path (Join-Path $Root ([string]$_))
        }
    })
    $material = @($entries | ForEach-Object { "$($_.path):$($_.sha256)" }) -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($material)
    return [pscustomobject]@{
        count = $entries.Count
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    }
}

function Write-CloudFixtureBaseline {
    param([Parameter(Mandatory)][string]$Root)

    $workflowPaths = [string[]]@(
        '.github/workflows/cloud-ci.yml',
        '.github/workflows/cloud-image.yml'
    )
    $projectPaths = [string[]]@('src/tests/Sample.Tests/Sample.Tests.csproj')
    $workflowManifest = Get-TestManifest -Root $Root -Paths $workflowPaths
    $projectManifest = Get-TestManifest -Root $Root -Paths $projectPaths
    $emptyManifest = Get-TestManifest -Root $Root -Paths @()
    $cloudCiPath = Join-Path $Root '.github/workflows/cloud-ci.yml'
    $deploymentPath = Join-Path $Root 'deploy/tests/deployment-behavior.sh'
    $baseline = [pscustomobject][ordered]@{
        schemaVersion = '1.2'
        ruleId = 'CLOUD-TEST-GOV-001'
        protectedAssets = [object[]]@(
            [pscustomobject][ordered]@{
                path = '.github/workflows/cloud-ci.yml'
                sha256 = Get-TestSha256 -Path $cloudCiPath
            }
        )
        workflowManifest = $workflowManifest
        projectManifest = $projectManifest
        buildControlManifest = $emptyManifest
        restoreControlManifest = $emptyManifest
        frontendTestManifest = [pscustomobject][ordered]@{
            count = 0
            sha256 = [string]$emptyManifest.sha256
            runnerCases = 0
        }
        deploymentBehavior = [pscustomobject][ordered]@{
            sourceSha256 = Get-TestSha256 -Path $deploymentPath
            runnerCases = 1
        }
        projects = [object[]]@(
            [pscustomobject][ordered]@{
                projectPath = 'src/tests/Sample.Tests/Sample.Tests.csproj'
                baselineDeclarations = 1
                baselineExecutionTemplates = 1
                baselineProjectedCases = 1
                baselineRunnerCases = 1
                frozenSourceFiles = [string[]]@()
                frozenSourceHashes = [pscustomobject]@{}
                tests = [object[]]@(
                    [pscustomobject][ordered]@{
                        executionTypes = [object[]]@(
                            [pscustomobject]@{ name = 'Sample.Tests' }
                        )
                        projectedCases = 1
                    }
                )
                runnerCases = [string[]]@('Sample.Tests.Sample')
            }
        )
    }
    Write-Utf8File `
        -Path (Join-Path $Root 'scripts/tests/baselines/cloud-test-governance.baseline.json') `
        -Content "$($baseline | ConvertTo-Json -Depth 100)`n"
}

function New-BaseFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "cloud-migration-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $script:TempRoots.Add($root)
    Invoke-Git -Root $root -Arguments @('init', '--quiet', '--initial-branch=main', '--object-format=sha1')
    Invoke-Git -Root $root -Arguments @('config', 'user.name', 'Cloud Migration Self Test')
    Invoke-Git -Root $root -Arguments @('config', 'user.email', 'cloud-migration@example.invalid')
    Invoke-Git -Root $root -Arguments @('config', 'commit.gpgsign', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.autocrlf', 'false')
    Invoke-Git -Root $root -Arguments @('config', 'core.safecrlf', 'true')
    $emptyHooks = Join-Path $root '.empty-git-hooks'
    [IO.Directory]::CreateDirectory($emptyHooks) | Out-Null
    Invoke-Git -Root $root -Arguments @('config', 'core.hooksPath', $emptyHooks)

    Write-Utf8File -Path (Join-Path $root 'IIoT.CloudPlatform.slnx') -Content @'
<Solution>
  <Project Path="src/tests/Sample.Tests/Sample.Tests.csproj" />
</Solution>
'@
    Write-Utf8File -Path (Join-Path $root 'src/tests/Sample.Tests/Sample.Tests.csproj') -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-Utf8File -Path (Join-Path $root 'src/tests/Sample.Tests/SampleTests.cs') -Content "public sealed class SampleTests { }`n"
    Write-Utf8File -Path (Join-Path $root '.github/workflows/cloud-ci.yml') -Content "name: old`n"
    Write-Utf8File -Path (Join-Path $root '.github/workflows/cloud-image.yml') -Content @'
name: old-image
on:
  workflow_dispatch:
jobs:
  build-image:
    runs-on: ubuntu-24.04
    steps:
      - run: echo image
'@
    Write-Utf8File -Path (Join-Path $root 'deploy/tests/deployment-behavior.sh') -Content @'
pass() {
  printf 'ok %s\n' "$1"
}
pass "$label"
'@
    Write-Utf8File -Path (Join-Path $root 'src/App/Program.cs') -Content "internal static class Program { }`n"
    $validatorTarget = Join-Path $root 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'
    $wrapperTarget = Join-Path $root 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
    $selfTarget = Join-Path $root 'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1'
    $schemaTarget = Join-Path $root 'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json'
    [IO.Directory]::CreateDirectory((Split-Path $validatorTarget -Parent)) | Out-Null
    [IO.File]::Copy($ValidatorPath, $validatorTarget, $true)
    [IO.File]::Copy($TrustedWrapperPath, $wrapperTarget, $true)
    [IO.File]::Copy($SelfPath, $selfTarget, $true)
    [IO.File]::Copy($SchemaPath, $schemaTarget, $true)
    Write-CloudFixtureBaseline -Root $root

    $base = Commit-All -Root $root -Message 'base'
    return [pscustomobject]@{ Root = $root; Base = $base }
}

function New-TemplateCandidate {
    param([Parameter(Mandatory)][object]$Fixture)

    Write-Utf8File -Path (Join-Path $Fixture.Root '.github/workflows/cloud-ci.yml') -Content "$(Get-RequiredWorkflowContent)`n"
    Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Program.cs') -Content "internal static class Program { internal const int Version = 2; }`n"
    Write-CloudFixtureBaseline -Root $Fixture.Root
    $template = Commit-All -Root $Fixture.Root -Message 'template candidate'
    $Fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $Fixture
}

function New-ReceiptJson {
    param([Parameter(Mandatory)][object]$Fixture)

    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $ValidatorPath,
        '-Mode', 'Describe',
        '-RepositoryRoot', $Fixture.Root,
        '-TrustedBaseRevision', $Fixture.Base,
        '-CandidateRevision', $Fixture.Template,
        '-MigrationId', $MigrationId,
        '-RuleIdsCsv', 'CLOUD-TEST-GOV-001B',
        '-Owner', 'Cloud.Architecture',
        '-ApprovedBy', 'ShuJinHao',
        '-Reason', 'Self-test receipt for one exact workflow migration.',
        '-IssuedAtUtc', $IssuedAtUtc,
        '-ExpiresAtUtc', $ExpiresAtUtc)
    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Describe failed: $($output -join [Environment]::NewLine)"
    }
    return ($output | Out-String).Trim()
}

function Invoke-DescribeResult {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$RuleIdsCsv
    )

    return Invoke-PowerShellResult -Arguments @(
        '-File', $ValidatorPath,
        '-Mode', 'Describe',
        '-RepositoryRoot', $Fixture.Root,
        '-TrustedBaseRevision', $Fixture.Base,
        '-CandidateRevision', $Fixture.Template,
        '-MigrationId', $MigrationId,
        '-RuleIdsCsv', $RuleIdsCsv,
        '-Owner', 'Cloud.Architecture',
        '-ApprovedBy', 'ShuJinHao',
        '-Reason', 'Self-test receipt for one exact workflow migration.',
        '-IssuedAtUtc', $IssuedAtUtc,
        '-ExpiresAtUtc', $ExpiresAtUtc)
}

function New-AuthorizationFixture {
    param(
        [scriptblock]$MutateReceipt,
        [scriptblock]$MutateTemplate,
        [switch]$AddAuthorizationNoise,
        [switch]$AddSecondPending
    )

    $fixture = New-TemplateCandidate -Fixture (New-BaseFixture)
    $receiptJson = New-ReceiptJson -Fixture $fixture
    if ($null -ne $MutateTemplate) {
        Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Template)
        & $MutateTemplate $fixture.Root
        Write-CloudFixtureBaseline -Root $fixture.Root
        Invoke-Git -Root $fixture.Root -Arguments @('add', '--all')
        Invoke-Git -Root $fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $fixture.Template = Invoke-Git -Root $fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
        $receipt = ConvertFrom-TestJson -Json $receiptJson
        $receipt.changes = [object[]]@(Get-TestGitDiffRecords `
            -Root $fixture.Root `
            -BaseRevision $fixture.Base `
            -TargetRevision $fixture.Template)
        $receiptJson = $receipt | ConvertTo-Json -Depth 100
    }
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    if ($null -ne $MutateReceipt) {
        $receiptJson = & $MutateReceipt $receiptJson
    }
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$receiptJson`n"
    if ($AddAuthorizationNoise) {
        Write-Utf8File -Path (Join-Path $fixture.Root 'docs/noise.md') -Content "authorization noise`n"
    }
    if ($AddSecondPending) {
        $second = $receiptJson.Replace($MigrationId, 'CLOUD-BASELINE-MIG-SELFTEST-002')
        Write-Utf8File -Path (Join-Path $fixture.Root 'scripts/governance/migrations/pending/CLOUD-BASELINE-MIG-SELFTEST-002.json') -Content "$second`n"
    }
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize migration'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function New-TrustTemplateFixture {
    $fixture = New-BaseFixture
    Write-Utf8File -Path (Join-Path $fixture.Root '.github/workflows/cloud-ci.yml') -Content "$(Get-RequiredWorkflowContent)`n"
    Write-CloudFixtureBaseline -Root $fixture.Root
    $fixture.Base = Commit-All -Root $fixture.Root -Message 'integrated trusted workflow base'
    [IO.File]::AppendAllText(
        (Join-Path $fixture.Root 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'),
        "# reviewed trust upgrade candidate`n",
        [Text.UTF8Encoding]::new($false))
    $template = Commit-All -Root $fixture.Root -Message 'trust upgrade template'
    $fixture | Add-Member -NotePropertyName Template -NotePropertyValue $template -Force
    return $fixture
}

function New-TrustUpgradeFixture {
    $fixture = New-TrustTemplateFixture
    $result = Invoke-DescribeResult `
        -Fixture $fixture `
        -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'
    if ($result.ExitCode -ne 0) {
        throw "Trust upgrade Describe failed: $($result.Output)"
    }
    Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
    Write-Utf8File -Path (Join-Path $fixture.Root $ReceiptRelativePath) -Content "$($result.Output)`n"
    $authorization = Commit-All -Root $fixture.Root -Message 'authorize trust upgrade'
    $fixture | Add-Member -NotePropertyName Authorization -NotePropertyValue $authorization -Force
    return $fixture
}

function Complete-Cancellation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$AlterCancelled,
        [switch]$AddExtraPath,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    $pending = Join-Path $Fixture.Root $ReceiptRelativePath
    $cancelled = Join-Path $Fixture.Root $CancelledRelativePath
    [IO.Directory]::CreateDirectory((Split-Path $cancelled -Parent)) | Out-Null
    [IO.File]::Move($pending, $cancelled)
    if ($AlterCancelled) {
        [IO.File]::AppendAllText($cancelled, " `n", [Text.UTF8Encoding]::new($false))
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'docs/cancellation-noise.md') -Content "noise`n"
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $CancelledRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '-m', 'cancel migration')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Complete-Candidate {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [switch]$SkipMove,
        [switch]$AlterConsumed,
        [switch]$AddExtraPath,
        [switch]$RemoveExpectedPath,
        [switch]$ModifyCandidateValidator
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    Invoke-Git -Root $Fixture.Root -Arguments @('cherry-pick', '--quiet', $Fixture.Template)
    if ($RemoveExpectedPath) {
        Invoke-Git -Root $Fixture.Root -Arguments @('checkout', "$($Fixture.Authorization)^", '--', '.github/workflows/cloud-ci.yml')
    }
    if (-not $SkipMove) {
        $pending = Join-Path $Fixture.Root $ReceiptRelativePath
        $consumed = Join-Path $Fixture.Root $ConsumedRelativePath
        [IO.Directory]::CreateDirectory((Split-Path $consumed -Parent)) | Out-Null
        [IO.File]::Move($pending, $consumed)
        if ($AlterConsumed) {
            [IO.File]::AppendAllText($consumed, " `n", [Text.UTF8Encoding]::new($false))
        }
    }
    if ($AddExtraPath) {
        Write-Utf8File -Path (Join-Path $Fixture.Root 'src/App/Extra.cs') -Content "internal sealed class Extra { }`n"
    }
    if ($ModifyCandidateValidator) {
        [IO.File]::AppendAllText(
            (Join-Path $Fixture.Root 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'),
            "# candidate bypass`n",
            [Text.UTF8Encoding]::new($false))
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $candidate = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $Fixture | Add-Member -NotePropertyName Candidate -NotePropertyValue $candidate -Force
    return $Fixture
}

function Invoke-Validation {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Candidate,
        [string]$Relationship = 'BaseAncestorOfHead'
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Candidate)
    $executorPath = $TrustedWrapperPath
    $temporaryWrapper = $null
    if ($Base -match '^[0-9A-Fa-f]{40}$') {
        & git -C $Fixture.Root cat-file -e "$Base^{commit}" 2>$null
        if ($LASTEXITCODE -eq 0) {
            $entry = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('ls-tree', $Base, '--', $TrustedWrapperRelativePath) `
                -Capture
            $entryMatch = [regex]::Match(
                $entry,
                '^100644 blob (?<ObjectId>[0-9a-f]+)\t' +
                    [regex]::Escape($TrustedWrapperRelativePath) + '$')
            if (-not $entryMatch.Success) {
                throw 'Trusted self-test base does not contain the reviewed wrapper.'
            }
            $temporaryWrapper = Join-Path ([IO.Path]::GetTempPath()) (
                "$([Guid]::NewGuid().ToString('N')).trusted-wrapper.ps1")
            Export-TestGitBlob `
                -Root $Fixture.Root `
                -ObjectId $entryMatch.Groups['ObjectId'].Value `
                -Destination $temporaryWrapper
            $actualObjectId = Invoke-Git `
                -Root $Fixture.Root `
                -Arguments @('hash-object', '--no-filters', '--', $temporaryWrapper) `
                -Capture
            if ($actualObjectId -cne $entryMatch.Groups['ObjectId'].Value) {
                throw 'Extracted self-test wrapper differs from its trusted Git blob.'
            }
            $executorPath = $temporaryWrapper
        }
    }

    try {
        $arguments = @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-File', $executorPath,
            '-RepositoryRoot', $Fixture.Root,
            '-TrustedBaseRevision', $Base,
            '-CandidateRevision', $Candidate,
            '-AnchorRelationship', $Relationship)
        $output = & pwsh @arguments 2>&1
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String).Trim()
        }
    }
    finally {
        if ($null -ne $temporaryWrapper) {
            Remove-Item $temporaryWrapper -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-PowerShellResult {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & pwsh -NoLogo -NoProfile -NonInteractive @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Amend-AuthorizationReceiptBytes {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [switch]$ExecutableMode
    )

    Invoke-Git -Root $Fixture.Root -Arguments @('checkout', '--quiet', $Fixture.Authorization)
    [IO.File]::WriteAllBytes((Join-Path $Fixture.Root $ReceiptRelativePath), $Bytes)
    Invoke-Git -Root $Fixture.Root -Arguments @('add', '--all')
    if ($ExecutableMode) {
        Invoke-Git -Root $Fixture.Root -Arguments @('update-index', '--chmod=+x', '--', $ReceiptRelativePath)
    }
    Invoke-Git -Root $Fixture.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $Fixture.Authorization = Invoke-Git -Root $Fixture.Root -Arguments @('rev-parse', 'HEAD') -Capture
    return $Fixture
}

function Assert-Pass {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action,
        [string]$ExpectedText
    )

    try {
        $result = & $Action
        if ($result.ExitCode -ne 0) {
            throw "expected success but exit=$($result.ExitCode): $($result.Output)"
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedText) -and
            $result.Output -notmatch [regex]::Escape($ExpectedText)) {
            throw "expected output '$ExpectedText' but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedCode,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        $result = & $Action
        if ($result.ExitCode -eq 0) {
            throw "expected rejection but validator passed: $($result.Output)"
        }
        if ($result.Output -notmatch [regex]::Escape("CLOUD-BASELINE-MIG-001-$ExpectedCode")) {
            throw "expected $ExpectedCode but got: $($result.Output)"
        }
        $script:Passed++
        Write-Host "PASS $Name (rejected $ExpectedCode)"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name -- $($_.Exception.Message)"
    }
}

function Get-AggregateRejectionResult {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Results,
        [Parameter(Mandatory)][string]$ExpectedCode,
        [Parameter(Mandatory)][string]$Context
    )

    if ($Results.Count -eq 0) { throw "$Context did not execute any rejection scenarios." }
    foreach ($result in $Results) {
        if ($result.ExitCode -eq 0) {
            throw "$Context unexpectedly passed: $($result.Output)"
        }
        if ($result.Output -notmatch [regex]::Escape("CLOUD-BASELINE-MIG-001-$ExpectedCode")) {
            throw "$Context expected $ExpectedCode but got: $($result.Output)"
        }
    }
    return [pscustomobject]@{
        ExitCode = 1
        Output = "CLOUD-BASELINE-MIG-001-$ExpectedCode aggregate rejection passed: $Context"
    }
}

function Get-ValidatorConstant {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Language.ScriptBlockAst]$Ast,
        [Parameter(Mandatory)][string]$VariableName
    )

    $assignments = @($Ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq $VariableName
    }, $true))
    if ($assignments.Count -ne 1 -or
        $assignments[0].Right -isnot [System.Management.Automation.Language.CommandExpressionAst]) {
        throw "validator constant '$VariableName' must have one literal AST assignment."
    }
    try {
        return $assignments[0].Right.Expression.SafeGetValue()
    }
    catch {
        throw "validator constant '$VariableName' is not AST-safe literal data: $($_.Exception.Message)"
    }
}

try {
    Assert-Pass -Name 'reference schema locks reviewed receipt constants' -Action {
        $schema = ConvertFrom-TestJson -Json ([IO.File]::ReadAllText($SchemaPath))
        $validatorTokens = $null
        $validatorErrors = $null
        $validatorAst = [Management.Automation.Language.Parser]::ParseFile(
            $ValidatorPath,
            [ref]$validatorTokens,
            [ref]$validatorErrors)
        if ($validatorErrors.Count -ne 0) {
            throw "candidate validator has parser errors: $(@($validatorErrors.Message) -join '; ')."
        }
        $required = @($schema.required)
        $expectedRequired = @(
            'schemaVersion', 'ruleId', 'migrationId', 'issuedAgainstRevision',
            'issuedAtUtc', 'expiresAtUtc', 'owner', 'approvedBy', 'reason',
            'ruleIds', 'source', 'target', 'projectChanges', 'changes')
        $expectedOwners = [string[]]@(
            'Cloud.Architecture', 'Cloud.Deployment', 'Cloud.Infrastructure',
            'Cloud.Persistence', 'Cloud.Security', 'Cloud.Tests')
        $expectedRuleIds = [string[]]@(
            'CLOUD-TEST-GOV-001B', 'CLOUD-CACHE-001', 'CLOUD-ARCH-001',
            'CLOUD-TEST-002', 'CLOUD-TEST-003', 'CLOUD-TEST-004',
            'CLOUD-TEST-005', 'CLOUD-TEST-CLEANUP',
            'CLOUD-BASELINE-TRUST-UPGRADE-001')
        $expectedCountFields = [string[]]@(
            'repositoryProjects', 'testProjects', 'testSourceFiles',
            'frozenSourceFiles', 'declarations', 'executionTemplates',
            'projectedCases', 'runnerCases', 'protectedAssets', 'workflowFiles',
            'projectFiles', 'buildControlFiles', 'restoreControlFiles',
            'frontendTestFiles', 'frontendRunnerCases',
            'deploymentSourceDeclarations', 'deploymentRunnerCases')
        $expectedApprovers = [string[]]@('ShuJinHao')
        $expectedDescribeArguments = [string[]]@(
            'MigrationId', 'RuleIdsCsv', 'Owner', 'ApprovedBy', 'Reason')
        $expectedCanonicalWorkflowPath = '.github/workflows/cloud-ci.yml'
        $expectedCanonicalWorkflowName = 'cloud-ci'
        $expectedRequiredFinalJobId = 'required-final'
        $expectedWorkflowIdentityKeyPattern = '^[A-Za-z_][A-Za-z0-9_-]*$'
        $expectedWorkflowIdentityValuePattern = '^[A-Za-z0-9](?:[A-Za-z0-9._ /-]{0,126}[A-Za-z0-9._/-])?$'
        $expectedReservedWorkflowTrustReferenceTokens = [string[]]@(
            'cloud-ci',
            'migration-validator-selftest',
            'build-test',
            'required-final',
            'CLOUD-BASELINE-MIG-TRUSTED-EXECUTOR-V1',
            'CLOUD-BASELINE-MIG-ISOLATED-SELFTEST-V1',
            'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1',
            'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1',
            'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1',
            'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json')
        $runtimeSchemaVersion = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ReceiptSchemaVersion')
        $runtimeRuleId = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:RuleId')
        $runtimeMaximumChanges = [long](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:MaximumReceiptChanges')
        $runtimeOwners = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ApprovedOwners')
        $runtimeApprovers = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ApprovedApprovers')
        $runtimeRuleIds = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ApprovedGovernedRuleIds')
        $runtimeCountFields = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ReceiptCountFields')
        $runtimeDescribeArguments = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:DescribeRequiredArgumentNames')
        $runtimeDescribeRelationship = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:DescribeAnchorRelationship')
        $runtimeCanonicalWorkflowPath = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:CanonicalWorkflowPath')
        $runtimeCanonicalWorkflowName = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:CanonicalWorkflowName')
        $runtimeRequiredFinalJobId = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:RequiredFinalJobId')
        $runtimeWorkflowIdentityKeyPattern = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:WorkflowIdentityKeyPattern')
        $runtimeWorkflowIdentityValuePattern = [string](Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:WorkflowIdentityValuePattern')
        $runtimeReservedWorkflowTrustReferenceTokens = [string[]]@(Get-ValidatorConstant `
            -Ast $validatorAst `
            -VariableName '$script:ReservedWorkflowTrustReferenceTokens')
        $actualOwners = [string[]]@($schema.properties.owner.enum)
        $actualRuleIds = [string[]]@($schema.properties.ruleIds.items.enum)
        $actualCountFields = [string[]]@($schema.'$defs'.counts.required)
        $actualRootProperties = [string[]]@(
            $schema.properties.PSObject.Properties | ForEach-Object { [string]$_.Name })
        $actualCountProperties = [string[]]@(
            $schema.'$defs'.counts.properties.PSObject.Properties |
                ForEach-Object { [string]$_.Name })
        $expectedRootProperties = [string[]]@($expectedRequired)
        [Array]::Sort($expectedRootProperties, [StringComparer]::Ordinal)
        [Array]::Sort($actualRootProperties, [StringComparer]::Ordinal)
        $actualRequiredText = (@($required | Sort-Object) -join "`n")
        $expectedRequiredText = (@($expectedRequired | Sort-Object) -join "`n")
        if ($schema.additionalProperties -ne $false -or
            $actualRequiredText -cne $expectedRequiredText -or
            ($actualRootProperties -join "`n") -cne ($expectedRootProperties -join "`n") -or
            $runtimeSchemaVersion -cne '1.0' -or
            [string]$schema.properties.schemaVersion.const -cne $runtimeSchemaVersion -or
            $runtimeRuleId -cne 'CLOUD-BASELINE-MIG-001' -or
            [string]$schema.properties.ruleId.const -cne $runtimeRuleId -or
            ($runtimeApprovers -join "`n") -cne ($expectedApprovers -join "`n") -or
            [string]$schema.properties.approvedBy.const -cne $runtimeApprovers[0] -or
            $runtimeMaximumChanges -ne 5000 -or
            [long]$schema.properties.changes.maxItems -ne $runtimeMaximumChanges -or
            ($runtimeOwners -join "`n") -cne ($expectedOwners -join "`n") -or
            ($actualOwners -join "`n") -cne ($expectedOwners -join "`n") -or
            ($actualOwners -join "`n") -cne ($runtimeOwners -join "`n") -or
            ($runtimeRuleIds -join "`n") -cne ($expectedRuleIds -join "`n") -or
            ($actualRuleIds -join "`n") -cne ($expectedRuleIds -join "`n") -or
            ($actualRuleIds -join "`n") -cne ($runtimeRuleIds -join "`n") -or
            ($runtimeCountFields -join "`n") -cne ($expectedCountFields -join "`n") -or
            ($actualCountFields -join "`n") -cne ($expectedCountFields -join "`n") -or
            ($actualCountFields -join "`n") -cne ($runtimeCountFields -join "`n") -or
            ($actualCountProperties -join "`n") -cne ($expectedCountFields -join "`n") -or
            ($runtimeDescribeArguments -join "`n") -cne ($expectedDescribeArguments -join "`n") -or
            $runtimeDescribeRelationship -cne 'BaseAncestorOfHead' -or
            $runtimeCanonicalWorkflowPath -cne $expectedCanonicalWorkflowPath -or
            $runtimeCanonicalWorkflowName -cne $expectedCanonicalWorkflowName -or
            $runtimeRequiredFinalJobId -cne $expectedRequiredFinalJobId -or
            $runtimeWorkflowIdentityKeyPattern -cne $expectedWorkflowIdentityKeyPattern -or
            $runtimeWorkflowIdentityValuePattern -cne $expectedWorkflowIdentityValuePattern -or
            ($runtimeReservedWorkflowTrustReferenceTokens -join "`n") -cne
                ($expectedReservedWorkflowTrustReferenceTokens -join "`n") -or
            @($schema.'$defs'.state.required) -cnotcontains 'manifests' -or
            -not ([string]$schema.'$comment').Contains('is authoritative', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'reference schema constants drifted from the runtime validator contract.'
        }
        return [pscustomobject]@{ ExitCode = 0; Output = 'schema parity passed' }
    }

    $immutable = New-BaseFixture
    Assert-Pass -Name 'immutable transition' -ExpectedText 'transition is immutable' -Action {
        Invoke-Validation -Fixture $immutable -Base $immutable.Base -Candidate $immutable.Base
    }

    $withoutReceipt = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'protected change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $withoutReceipt -Base $withoutReceipt.Base -Candidate $withoutReceipt.Template
    }

    $workflowPolicy = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $workflowPolicy.Root '.github/workflows/cloud-ci.yml') `
        -Content "$(Get-RequiredWorkflowContent)`n"
    Write-Utf8File `
        -Path (Join-Path $workflowPolicy.Root 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Content "# reviewed transition policy`n"
    $workflowPolicy | Add-Member `
        -NotePropertyName Template `
        -NotePropertyValue (Commit-All -Root $workflowPolicy.Root -Message 'workflow and policy candidate')
    Assert-Pass -Name 'workflow and policy can be described without baseline co-change' -Action {
        Invoke-DescribeResult -Fixture $workflowPolicy -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
    }

    $baselinePolicy = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $baselinePolicy.Root '.github/workflows/cloud-ci.yml') `
        -Content "$(Get-RequiredWorkflowContent)`n"
    Write-CloudFixtureBaseline -Root $baselinePolicy.Root
    [IO.File]::AppendAllText(
        (Join-Path $baselinePolicy.Root 'scripts/tests/baselines/cloud-test-governance.baseline.json'),
        "`n",
        [Text.UTF8Encoding]::new($false))
    Write-Utf8File `
        -Path (Join-Path $baselinePolicy.Root 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Content "# forbidden same-receipt policy candidate`n"
    $baselinePolicy | Add-Member `
        -NotePropertyName Template `
        -NotePropertyValue (Commit-All -Root $baselinePolicy.Root -Message 'baseline and policy candidate')
    Assert-Rejected -Name 'baseline and policy cannot change in one receipt' -ExpectedCode 'POLICY' -Action {
        Invoke-DescribeResult -Fixture $baselinePolicy -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
    }

    $describeRules = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Rejected -Name 'RuleIdsCsv rejects whitespace' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'CLOUD-ARCH-001, CLOUD-TEST-002'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects empty item' -ExpectedCode 'DESCRIBE' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'CLOUD-ARCH-001,,CLOUD-TEST-002'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects lowercase rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'cloud-arch-001'
    }
    Assert-Rejected -Name 'RuleIdsCsv rejects unregistered rule ID' -ExpectedCode 'RECEIPT' -Action {
        Invoke-DescribeResult -Fixture $describeRules -RuleIdsCsv 'AAA-001'
    }
    Assert-Pass -Name 'RuleIdsCsv output is ordinal-sorted and unique' -Action {
        $result = Invoke-DescribeResult `
            -Fixture $describeRules `
            -RuleIdsCsv 'CLOUD-CACHE-001,CLOUD-ARCH-001,CLOUD-ARCH-001'
        if ($result.ExitCode -eq 0) {
            $receipt = ConvertFrom-TestJson -Json $result.Output
            $actual = @($receipt.ruleIds)
            if ($actual.Count -ne 2 -or
                $actual[0] -cne 'CLOUD-ARCH-001' -or
                $actual[1] -cne 'CLOUD-CACHE-001') {
                throw "unexpected normalized ruleIds: $($actual -join ',')"
            }
        }
        return $result
    }

    $stateClosure = New-TemplateCandidate -Fixture (New-BaseFixture)
    Assert-Pass -Name 'Cloud receipt state closes backend and manifest inventories' -Action {
        $result = Invoke-DescribeResult -Fixture $stateClosure -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
        if ($result.ExitCode -eq 0) {
            $receipt = ConvertFrom-TestJson -Json $result.Output
            $counts = $receipt.target.counts
            $expectedCounts = [ordered]@{
                repositoryProjects = 1
                testProjects = 1
                testSourceFiles = 1
                frozenSourceFiles = 0
                declarations = 1
                executionTemplates = 1
                projectedCases = 1
                runnerCases = 1
                protectedAssets = 1
                workflowFiles = 2
                projectFiles = 1
                buildControlFiles = 0
                restoreControlFiles = 0
                frontendTestFiles = 0
                frontendRunnerCases = 0
                deploymentSourceDeclarations = 1
                deploymentRunnerCases = 1
            }
            $actualCountNames = [string[]]@(
                $counts.PSObject.Properties | ForEach-Object { [string]$_.Name })
            $expectedCountNames = [string[]]@($expectedCounts.Keys)
            [Array]::Sort($actualCountNames, [StringComparer]::Ordinal)
            [Array]::Sort($expectedCountNames, [StringComparer]::Ordinal)
            if (($actualCountNames -join "`n") -cne ($expectedCountNames -join "`n")) {
                throw "runtime count roster drifted: $($actualCountNames -join ',')."
            }
            foreach ($entry in $expectedCounts.GetEnumerator()) {
                if ([long]$counts.($entry.Key) -ne [long]$entry.Value) {
                    throw "unexpected Cloud state count $($entry.Key)=$($counts.($entry.Key)); expected=$($entry.Value)."
                }
            }
            foreach ($name in @(
                'protectedAssetsSha256', 'workflowSha256', 'projectSha256',
                'buildControlSha256', 'restoreControlSha256', 'frontendTestSha256',
                'deploymentSourceSha256')) {
                if ([string]$receipt.target.manifests.$name -cnotmatch '^[0-9a-f]{64}$') {
                    throw "target.manifests.$name is not an exact SHA-256."
                }
            }
        }
        return $result
    }

    $duplicateBaseline = New-TemplateCandidate -Fixture (New-BaseFixture)
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('checkout', '--quiet', $duplicateBaseline.Template)
    $baselinePath = Join-Path $duplicateBaseline.Root 'scripts/tests/baselines/cloud-test-governance.baseline.json'
    $baselineJson = [IO.File]::ReadAllText($baselinePath).Replace(
        '"projects":',
        '"projects":[],"projects":')
    Write-Utf8File -Path $baselinePath -Content $baselineJson
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('add', '--all')
    Invoke-Git -Root $duplicateBaseline.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $duplicateBaseline.Template = Invoke-Git -Root $duplicateBaseline.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'baseline duplicate JSON key is rejected' -ExpectedCode 'COUNTS' -Action {
        Invoke-DescribeResult -Fixture $duplicateBaseline -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
    }

    $valid = New-AuthorizationFixture
    Assert-Pass -Name 'authorization-only transition' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Base -Candidate $valid.Authorization
    }
    $valid = Complete-Candidate -Fixture $valid
    Assert-Pass -Name 'receipt consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $valid -Base $valid.Authorization -Candidate $valid.Candidate
    }

    $noise = New-AuthorizationFixture -AddAuthorizationNoise
    Assert-Rejected -Name 'authorization commit contains another file' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $noise -Base $noise.Base -Candidate $noise.Authorization
    }

    $second = New-AuthorizationFixture -AddSecondPending
    Assert-Rejected -Name 'two pending receipts in one authorization' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $second -Base $second.Base -Candidate $second.Authorization
    }

    $missing = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.PSObject.Properties.Remove('reason')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'missing receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $missing -Base $missing.Base -Candidate $missing.Authorization
    }

    $unknown = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt | Add-Member -NotePropertyName command -NotePropertyValue 'Invoke-Expression'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'unknown executable receipt field' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $unknown -Base $unknown.Base -Candidate $unknown.Authorization
    }

    $duplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"reason":"duplicate","reason":'
    }
    Assert-Rejected -Name 'duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $duplicate -Base $duplicate.Base -Candidate $duplicate.Authorization
    }

    $expired = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'expired receipt' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $expired -Base $expired.Base -Candidate $expired.Authorization
    }

    $tooLong = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.expiresAtUtc = $Now.AddDays(8).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'receipt lifetime exceeds seven days' -ExpectedCode 'EXPIRY' -Action {
        Invoke-Validation -Fixture $tooLong -Base $tooLong.Base -Candidate $tooLong.Authorization
    }

    $wrongBase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAgainstRevision = '1111111111111111111111111111111111111111'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wrong issued-against revision' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $wrongBase -Base $wrongBase.Base -Candidate $wrongBase.Authorization
    }

    $traversal = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = '../escape.yml'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'path traversal in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $traversal -Base $traversal.Base -Candidate $traversal.Authorization
    }

    $wildcard = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/*.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'wildcard path in receipt' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $wildcard -Base $wildcard.Base -Candidate $wildcard.Authorization
    }

    $wrongMode = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterMode = '120000'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'symlink mode in receipt' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongMode -Base $wrongMode.Base -Candidate $wrongMode.Authorization
    }

    $caseCollision = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $clone = ConvertFrom-TestJson -Json ($receipt.changes[0] | ConvertTo-Json -Depth 10)
        $clone.path = ([string]$clone.path).ToUpperInvariant()
        $receipt.changes = @($receipt.changes) + @($clone)
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'case-colliding receipt paths' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $caseCollision -Base $caseCollision.Base -Candidate $caseCollision.Authorization
    }

    $wrongHash = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].afterSha256 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization accepts reviewed future descriptor' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Base -Candidate $wrongHash.Authorization
    }
    $wrongHash = Complete-Candidate -Fixture $wrongHash
    Assert-Rejected -Name 'consumption rejects wrong file hash' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $wrongHash -Base $wrongHash.Authorization -Candidate $wrongHash.Candidate
    }

    $countDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [int]$receipt.target.counts.runnerCases + 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Pass -Name 'authorization records future count claim' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Base -Candidate $countDrift.Authorization
    }
    $countDrift = Complete-Candidate -Fixture $countDrift
    Assert-Rejected -Name 'consumption rejects runner count drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $countDrift -Base $countDrift.Authorization -Candidate $countDrift.Candidate
    }

    $manifestDrift = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.protectedManifestSha256 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        return $receipt | ConvertTo-Json -Depth 100
    }
    $manifestDrift = Complete-Candidate -Fixture $manifestDrift
    Assert-Rejected -Name 'consumption rejects protected manifest drift' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $manifestDrift -Base $manifestDrift.Authorization -Candidate $manifestDrift.Candidate
    }

    foreach ($manifestName in @(
        'protectedAssetsSha256', 'workflowSha256', 'projectSha256',
        'buildControlSha256', 'restoreControlSha256', 'frontendTestSha256',
        'deploymentSourceSha256')) {
        $mutatedManifestName = $manifestName
        $manifestStateDrift = New-AuthorizationFixture -MutateReceipt {
            param($json)
            $receipt = ConvertFrom-TestJson -Json $json
            $receipt.target.manifests.$mutatedManifestName =
                'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'
            return $receipt | ConvertTo-Json -Depth 100
        }
        $manifestStateDrift = Complete-Candidate -Fixture $manifestStateDrift
        Assert-Rejected -Name "consumption rejects $manifestName drift" -ExpectedCode 'MISMATCH' -Action {
            Invoke-Validation `
                -Fixture $manifestStateDrift `
                -Base $manifestStateDrift.Authorization `
                -Candidate $manifestStateDrift.Candidate
        }
    }

    $noMove = Complete-Candidate -Fixture (New-AuthorizationFixture) -SkipMove
    Assert-Rejected -Name 'pending receipt not moved' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noMove -Base $noMove.Authorization -Candidate $noMove.Candidate
    }

    $altered = Complete-Candidate -Fixture (New-AuthorizationFixture) -AlterConsumed
    Assert-Rejected -Name 'consumed receipt blob changed' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $altered -Base $altered.Authorization -Candidate $altered.Candidate
    }

    $extra = Complete-Candidate -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'candidate has extra path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $extra -Base $extra.Authorization -Candidate $extra.Candidate
    }

    $fewer = Complete-Candidate -Fixture (New-AuthorizationFixture) -RemoveExpectedPath
    Assert-Rejected -Name 'candidate omits expected path' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $fewer -Base $fewer.Authorization -Candidate $fewer.Candidate
    }

    $candidateBypass = Complete-Candidate -Fixture (New-AuthorizationFixture) -ModifyCandidateValidator
    Assert-Rejected -Name 'candidate validator cannot self-authorize' -ExpectedCode 'MISMATCH' -Action {
        Invoke-Validation -Fixture $candidateBypass -Base $candidateBypass.Authorization -Candidate $candidateBypass.Candidate
    }

    $wrongPropertyCase = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"reason"\s*:', '"Reason":'
    }
    Assert-Rejected -Name 'receipt property names are case-sensitive' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $wrongPropertyCase -Base $wrongPropertyCase.Base -Candidate $wrongPropertyCase.Authorization
    }

    $nestedDuplicate = New-AuthorizationFixture -MutateReceipt {
        param($json)
        return $json -replace '"baselineSha256"\s*:', '"baselineSha256":"duplicate","baselineSha256":'
    }
    Assert-Rejected -Name 'nested duplicate JSON key' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nestedDuplicate -Base $nestedDuplicate.Base -Candidate $nestedDuplicate.Authorization
    }

    $scalarRuleIds = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.ruleIds = 'CLOUD-ARCH-001'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'ruleIds scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarRuleIds -Base $scalarRuleIds.Base -Candidate $scalarRuleIds.Authorization
    }

    $scalarChanges = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes = $receipt.changes[0]
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'changes scalar is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $scalarChanges -Base $scalarChanges.Base -Candidate $scalarChanges.Authorization
    }

    $nullProjectArray = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.projectChanges.added = $null
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'null project array is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $nullProjectArray -Base $nullProjectArray.Base -Candidate $nullProjectArray.Authorization
    }

    foreach ($registryMutation in @(
        @{ Name = 'owner registry is case-sensitive'; Property = 'owner'; Value = 'cloud.architecture' },
        @{ Name = 'owner outside migration registry is rejected'; Property = 'owner'; Value = 'Cloud.Identity' },
        @{ Name = 'approver registry is case-sensitive'; Property = 'approvedBy'; Value = 'shujinhao' },
        @{ Name = 'governance rule ID is case-sensitive'; Property = 'ruleId'; Value = 'cloud-baseline-mig-001' }
    )) {
        $propertyName = [string]$registryMutation.Property
        $propertyValue = [string]$registryMutation.Value
        $registryCase = New-AuthorizationFixture -MutateReceipt {
            param($json)
            $receipt = ConvertFrom-TestJson -Json $json
            $receipt.$propertyName = $propertyValue
            return $receipt | ConvertTo-Json -Depth 100
        }
        Assert-Rejected -Name ([string]$registryMutation.Name) -ExpectedCode 'RECEIPT' -Action {
            Invoke-Validation -Fixture $registryCase -Base $registryCase.Base -Candidate $registryCase.Authorization
        }
    }

    $windowsReserved = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/CON.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows reserved path is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $windowsReserved -Base $windowsReserved.Base -Candidate $windowsReserved.Authorization
    }

    $trailingDot = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/bad./Case.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows trailing-dot segment is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $trailingDot -Base $trailingDot.Base -Candidate $trailingDot.Authorization
    }

    $superscriptDevice = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = 'src/tests/COM¹.cs'
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows superscript device name is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $superscriptDevice -Base $superscriptDevice.Base -Candidate $superscriptDevice.Authorization
    }

    $longComponent = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.changes[0].path = "src/tests/$('a' * 256).cs"
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'Windows overlong path component is rejected' -ExpectedCode 'PATH' -Action {
        Invoke-Validation -Fixture $longComponent -Base $longComponent.Base -Candidate $longComponent.Authorization
    }

    $countDecrease = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.target.counts.runnerCases = [long]$receipt.source.counts.runnerCases - 1
        return $receipt | ConvertTo-Json -Depth 100
    }
    Assert-Rejected -Name 'target test evidence cannot decrease' -ExpectedCode 'COUNTS' -Action {
        Invoke-Validation -Fixture $countDecrease -Base $countDecrease.Base -Candidate $countDecrease.Authorization
    }

    $unfrozenSource = New-BaseFixture
    $frozenRelativePath = 'src/tests/Sample.Tests/FrozenContractTests.cs'
    $frozenAbsolutePath = Join-Path $unfrozenSource.Root $frozenRelativePath
    Write-Utf8File -Path $frozenAbsolutePath -Content "public sealed class FrozenContractTests { }`n"
    $frozenBaselinePath = Join-Path $unfrozenSource.Root 'scripts/tests/baselines/cloud-test-governance.baseline.json'
    $frozenBaseline = ConvertFrom-TestJson -Json ([IO.File]::ReadAllText($frozenBaselinePath))
    $frozenHashes = [ordered]@{}
    $frozenHashes[$frozenRelativePath] = Get-TestSha256 -Path $frozenAbsolutePath
    $frozenBaseline.projects[0].frozenSourceFiles = [string[]]@($frozenRelativePath)
    $frozenBaseline.projects[0].frozenSourceHashes = [pscustomobject]$frozenHashes
    Write-Utf8File -Path $frozenBaselinePath -Content "$($frozenBaseline | ConvertTo-Json -Depth 100)`n"
    Invoke-Git -Root $unfrozenSource.Root -Arguments @('add', '--all')
    Invoke-Git -Root $unfrozenSource.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $unfrozenSource.Base = Invoke-Git -Root $unfrozenSource.Root -Arguments @('rev-parse', 'HEAD') -Capture
    $unfrozenSource = New-TemplateCandidate -Fixture $unfrozenSource
    Assert-Rejected -Name 'baseline-only transition cannot unfreeze reviewed source' -ExpectedCode 'COUNTS' -Action {
        Invoke-DescribeResult `
            -Fixture $unfrozenSource `
            -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
    }

    $invalidUtf8 = New-AuthorizationFixture
    $invalidUtf8 = Amend-AuthorizationReceiptBytes -Fixture $invalidUtf8 -Bytes ([byte[]]@(0x7B, 0xFF, 0x7D))
    Assert-Rejected -Name 'invalid UTF-8 receipt is rejected' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $invalidUtf8 -Base $invalidUtf8.Base -Candidate $invalidUtf8.Authorization
    }

    $oversized = New-AuthorizationFixture
    $oversized = Amend-AuthorizationReceiptBytes -Fixture $oversized -Bytes (
        [Text.Encoding]::UTF8.GetBytes(' ' * (1MB + 1)))
    Assert-Rejected -Name 'oversized receipt is rejected before parsing' -ExpectedCode 'RECEIPT' -Action {
        Invoke-Validation -Fixture $oversized -Base $oversized.Base -Candidate $oversized.Authorization
    }

    $executablePending = New-AuthorizationFixture
    $pendingBytes = [IO.File]::ReadAllBytes((Join-Path $executablePending.Root $ReceiptRelativePath))
    $executablePending = Amend-AuthorizationReceiptBytes -Fixture $executablePending -Bytes $pendingBytes -ExecutableMode
    Assert-Rejected -Name 'pending receipt executable mode is rejected' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation -Fixture $executablePending -Base $executablePending.Base -Candidate $executablePending.Authorization
    }

    $missingTrustMarker = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            'CLOUD-BASELINE-MIG-TRUSTED-EXECUTOR-V1',
            'CLOUD-BASELINE-MIG-NOT-TRUSTED')
        Write-Utf8File -Path $path -Content $text
    }
    $missingTrustMarker = Complete-Candidate -Fixture $missingTrustMarker
    Assert-Rejected -Name 'workflow missing trusted marker is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $missingTrustMarker -Base $missingTrustMarker.Authorization -Candidate $missingTrustMarker.Candidate
    }

    $preGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted baseline migration',
            "      - name: Pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted baseline migration")
        Write-Utf8File -Path $path -Content $text
    }
    $preGateStep = Complete-Candidate -Fixture $preGateStep
    Assert-Rejected -Name 'workflow step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $preGateStep -Base $preGateStep.Authorization -Candidate $preGateStep.Candidate
    }

    $alternatePreGateStep = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate trusted baseline migration',
            "      -`n        name: Alternate pre-gate command`n        shell: pwsh`n        run: Write-Host bypass`n      - name: Validate trusted baseline migration")
        Write-Utf8File -Path $path -Content $text
    }
    $alternatePreGateStep = Complete-Candidate -Fixture $alternatePreGateStep
    Assert-Rejected -Name 'alternate YAML step before trusted gate is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $alternatePreGateStep `
            -Base $alternatePreGateStep.Authorization `
            -Candidate $alternatePreGateStep.Candidate
    }

    $spoofedCheckout = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '        uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7',
            "        uses: attacker/checkout@0123456789012345678901234567890123456789`n        # uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7")
        Write-Utf8File -Path $path -Content $text
    }
    $spoofedCheckout = Complete-Candidate -Fixture $spoofedCheckout
    Assert-Rejected -Name 'comment cannot spoof pinned checkout action' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $spoofedCheckout -Base $spoofedCheckout.Authorization -Candidate $spoofedCheckout.Candidate
    }

    $softFailedGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        continue-on-error: true`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $softFailedGate = Complete-Candidate -Fixture $softFailedGate
    Assert-Rejected -Name 'trusted gate cannot continue on error' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $softFailedGate -Base $softFailedGate.Authorization -Candidate $softFailedGate.Candidate
    }

    $conditionalGate = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Setup .NET',
            "        if: false`n      - name: Setup .NET")
        Write-Utf8File -Path $path -Content $text
    }
    $conditionalGate = Complete-Candidate -Fixture $conditionalGate
    Assert-Rejected -Name 'trusted gate cannot be conditional' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $conditionalGate -Base $conditionalGate.Authorization -Candidate $conditionalGate.Candidate
    }

    $changedRunner = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '    runs-on: ubuntu-24.04',
            '    runs-on: self-hosted')
        Write-Utf8File -Path $path -Content $text
    }
    $changedRunner = Complete-Candidate -Fixture $changedRunner
    Assert-Rejected -Name 'trusted job runner is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $changedRunner -Base $changedRunner.Authorization -Candidate $changedRunner.Candidate
    }

    $wrongCheckoutRef = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '          ref: ${{ github.event.pull_request.head.sha || github.sha }}',
            '          ref: ${{ github.sha }}')
        Write-Utf8File -Path $path -Content $text
    }
    $wrongCheckoutRef = Complete-Candidate -Fixture $wrongCheckoutRef
    Assert-Rejected -Name 'required workflow rejects synthetic merge checkout' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $wrongCheckoutRef `
            -Base $wrongCheckoutRef.Authorization `
            -Candidate $wrongCheckoutRef.Candidate
    }

    $manualBranchBypass = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "`$eventRef -ne 'refs/heads/main'",
            "`$eventRef -ne 'refs/heads/feature'")
        Write-Utf8File -Path $path -Content $text
    }
    $manualBranchBypass = Complete-Candidate -Fixture $manualBranchBypass
    Assert-Rejected -Name 'manual required workflow remains restricted to main' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $manualBranchBypass -Base $manualBranchBypass.Authorization -Candidate $manualBranchBypass.Candidate
    }

    $missingMigrationSelfTest = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1',
            'scripts/governance/migrations/SkippedCloudBaselineMigrationValidator.v1.ps1')
        Write-Utf8File -Path $path -Content $text
    }
    $missingMigrationSelfTest = Complete-Candidate -Fixture $missingMigrationSelfTest
    Assert-Rejected -Name 'required workflow cannot omit migration self-test' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $missingMigrationSelfTest `
            -Base $missingMigrationSelfTest.Authorization `
            -Candidate $missingMigrationSelfTest.Candidate
    }

    Assert-Rejected -Name 'required harness cannot use candidate wrapper schema self-test or workflow fixture' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($mutation in @(
            @{
                Old = '-TrustedWrapperPath $temporaryWrapper'
                New = '-TrustedWrapperPath ./scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1'
            },
            @{
                Old = '-SchemaPath $temporarySchema'
                New = '-SchemaPath ./scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json'
            },
            @{
                Old = '-ValidatorPath ./scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'
                New = '-CandidateSelfTestPath ./scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1'
            },
            @{
                Old = '-TrustedWorkflowPath $temporaryWorkflow'
                New = '-TrustedWorkflowPath ./.github/workflows/cloud-ci.yml'
            })) {
            $oldText = [string]$mutation.Old
            $newText = [string]$mutation.New
            $mutateProofInput = {
                param($root)
                $path = Join-Path $root '.github/workflows/cloud-ci.yml'
                $text = [IO.File]::ReadAllText($path)
                $changed = $text.Replace($oldText, $newText)
                if ($changed -ceq $text) { throw "workflow proof-input mutation did not match '$oldText'." }
                Write-Utf8File -Path $path -Content $changed
            }.GetNewClosure()
            $fixture = New-AuthorizationFixture -MutateTemplate $mutateProofInput
            $fixture = Complete-Candidate -Fixture $fixture
            $results.Add((Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate))
        }
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'candidate wrapper/schema/self-test/workflow proof inputs'
    }

    Assert-Rejected -Name 'all three required workflow job names are pinned' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($jobName in @(
            'migration-validator-selftest',
            'build-test',
            'required-final')) {
            $oldJobHeader = "  ${jobName}:`n"
            $newJobHeader = "  renamed-${jobName}:`n"
            $renameRequiredJob = {
                param($root)
                $path = Join-Path $root '.github/workflows/cloud-ci.yml'
                $text = [IO.File]::ReadAllText($path)
                $changed = $text.Replace($oldJobHeader, $newJobHeader)
                if ($changed -ceq $text) { throw "required job rename did not match '$oldJobHeader'." }
                Write-Utf8File -Path $path -Content $changed
            }.GetNewClosure()
            $fixture = New-AuthorizationFixture -MutateTemplate $renameRequiredJob
            $fixture = Complete-Candidate -Fixture $fixture
            $results.Add((Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate))
        }
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'three required workflow job names'
    }

    $changedWorkflowEnvelope = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "jobs:`n",
            "permissions: write-all`njobs:`n")
        Write-Utf8File -Path $path -Content $text
    }
    $changedWorkflowEnvelope = Complete-Candidate -Fixture $changedWorkflowEnvelope
    Assert-Rejected -Name 'workflow trigger and permissions envelope is pinned' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $changedWorkflowEnvelope `
            -Base $changedWorkflowEnvelope.Authorization `
            -Candidate $changedWorkflowEnvelope.Candidate
    }

    $missingStaticPolicy = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Validate reviewed restore and build inputs',
            '      - name: Candidate removed reviewed static policy')
        Write-Utf8File -Path $path -Content $text
    }
    $missingStaticPolicy = Complete-Candidate -Fixture $missingStaticPolicy
    Assert-Rejected -Name 'required workflow cannot remove static policy suffix' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $missingStaticPolicy -Base $missingStaticPolicy.Authorization -Candidate $missingStaticPolicy.Candidate
    }

    $missingReconciliation = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            '      - name: Reconcile required test results',
            '      - name: Candidate removed required reconciliation')
        Write-Utf8File -Path $path -Content $text
    }
    $missingReconciliation = Complete-Candidate -Fixture $missingReconciliation
    Assert-Rejected -Name 'required workflow cannot remove result reconciliation suffix' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $missingReconciliation -Base $missingReconciliation.Authorization -Candidate $missingReconciliation.Candidate
    }

    $detachedFullEndToEnd = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "  full-end-to-end:`n    needs: required-final`n",
            "  full-end-to-end:`n")
        Write-Utf8File -Path $path -Content $text
    }
    $detachedFullEndToEnd = Complete-Candidate -Fixture $detachedFullEndToEnd
    Assert-Rejected -Name 'full end-to-end job must depend on required gate' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $detachedFullEndToEnd -Base $detachedFullEndToEnd.Authorization -Candidate $detachedFullEndToEnd.Candidate
    }

    $parallelPrivilegedJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "jobs:`n  migration-validator-selftest:",
            "jobs:`n  candidate-write-job:`n    permissions: write-all`n    runs-on: ubuntu-latest`n    steps: []`n`n  migration-validator-selftest:")
        Write-Utf8File -Path $path -Content $text
    }
    $parallelPrivilegedJob = Complete-Candidate -Fixture $parallelPrivilegedJob
    Assert-Rejected -Name 'candidate cannot insert parallel privileged job before required job' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $parallelPrivilegedJob -Base $parallelPrivilegedJob.Authorization -Candidate $parallelPrivilegedJob.Candidate
    }

    Assert-Rejected -Name 'required final dependency and result checks are fail-closed' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($mutation in @(
            @{
                Old = '    needs: [migration-validator-selftest, build-test]'
                New = '    needs: build-test'
            },
            @{
                Old = '    if: always()'
                New = '    if: success()'
            },
            @{
                Old = '    timeout-minutes: 1'
                New = '    timeout-minutes: 25'
            },
            @{
                Old = '          test "$MIGRATION_VALIDATOR_SELFTEST_RESULT" = success'
                New = '          true # candidate bypassed migration validator result'
            },
            @{
                Old = '          test "$BUILD_TEST_RESULT" = success'
                New = '          true # candidate bypassed build result'
            })) {
            $oldText = [string]$mutation.Old
            $newText = [string]$mutation.New
            $mutateFinal = {
                param($root)
                $path = Join-Path $root '.github/workflows/cloud-ci.yml'
                $text = [IO.File]::ReadAllText($path)
                $changed = $text.Replace($oldText, $newText)
                if ($changed -ceq $text) { throw "required-final mutation did not match '$oldText'." }
                Write-Utf8File -Path $path -Content $changed
            }.GetNewClosure()
            $fixture = New-AuthorizationFixture -MutateTemplate $mutateFinal
            $fixture = Complete-Candidate -Fixture $fixture
            $results.Add((Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate))
        }
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'required-final fail-closed result aggregation'
    }

    $quotedJobLevelEnvironment = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "`n  full-end-to-end:",
            "`n    `"env`":`n      PATH: candidate-controlled-path`n`n  full-end-to-end:")
        Write-Utf8File `
            -Path $path `
            -Content $text
    }
    $quotedJobLevelEnvironment = Complete-Candidate -Fixture $quotedJobLevelEnvironment
    Assert-Rejected -Name 'quoted job-level environment cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedJobLevelEnvironment `
            -Base $quotedJobLevelEnvironment.Authorization `
            -Candidate $quotedJobLevelEnvironment.Candidate
    }

    $quotedTopLevelPermissions = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).TrimEnd("`r", "`n")
        Write-Utf8File `
            -Path $path `
            -Content "$text`n`"permissions`": write-all`n"
    }
    $quotedTopLevelPermissions = Complete-Candidate -Fixture $quotedTopLevelPermissions
    Assert-Rejected -Name 'quoted top-level permissions cannot bypass closure' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation `
            -Fixture $quotedTopLevelPermissions `
            -Base $quotedTopLevelPermissions.Authorization `
            -Candidate $quotedTopLevelPermissions.Candidate
    }

    $disabledTrustedJob = New-AuthorizationFixture -MutateTemplate {
        param($root)
        $path = Join-Path $root '.github/workflows/cloud-ci.yml'
        $text = [IO.File]::ReadAllText($path).Replace(
            "  build-test:`n    runs-on:",
            "  build-test:`n    if : false`n    runs-on:")
        Write-Utf8File -Path $path -Content $text
    }
    $disabledTrustedJob = Complete-Candidate -Fixture $disabledTrustedJob
    Assert-Rejected -Name 'disabled trusted workflow job is rejected' -ExpectedCode 'TRUST' -Action {
        Invoke-Validation -Fixture $disabledTrustedJob -Base $disabledTrustedJob.Authorization -Candidate $disabledTrustedJob.Candidate
    }

    Assert-Rejected -Name 'other workflows cannot duplicate reserved migration identity or proof paths' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($mutation in @(
            {
                param($root)
                $path = Join-Path $root '.github/workflows/cloud-image.yml'
                [IO.File]::AppendAllText(
                    $path,
                    "# scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1`n",
                    [Text.UTF8Encoding]::new($false))
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/duplicate-required-context.yml') `
                    -Content @'
name: cloud-ci
on:
  pull_request: {}
permissions:
  contents: read
jobs:
  required-final:
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/duplicate-selftest-context.yml') `
                    -Content @'
name: duplicate-selftest-context
on:
  pull_request: {}
permissions:
  contents: read
jobs:
  migration-validator-selftest:
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/encoded-selftest-job-name.yml') `
                    -Content @'
name: encoded-selftest-job-name
on:
  pull_request: {}
jobs:
  independent:
    name: "migration\u002dvalidator\u002dselftest"
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/duplicate-build-context.yml') `
                    -Content @'
name: duplicate-build-context
on:
  pull_request: {}
permissions:
  contents: read
jobs:
  build-test:
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/encoded-build-job-name.yml') `
                    -Content @'
name: encoded-build-job-name
on:
  pull_request: {}
jobs:
  independent:
    name: "build\u002dtest"
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                $path = Join-Path $root '.github/workflows/cloud-image.yml'
                [IO.File]::AppendAllText(
                    $path,
                    "# scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json`n",
                    [Text.UTF8Encoding]::new($false))
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/encoded-workflow-name.yml') `
                    -Content @'
name: "cloud\u002dci"
on:
  pull_request: {}
jobs:
  independent:
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/encoded-job-id.yml') `
                    -Content @'
name: encoded-job-id
on:
  pull_request: {}
jobs:
  "required\u002dfinal":
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/encoded-job-name.yml') `
                    -Content @'
name: encoded-job-name
on:
  pull_request: {}
jobs:
  independent:
    name: "required\u002dfinal"
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/block-expression-name.yml') `
                    -Content @'
name: block-expression-name
on:
  pull_request: {}
jobs:
  independent:
    name: >-
      ${{ format('{0}-{1}', 'required', 'final') }}
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/flow-jobs.yml') `
                    -Content @'
name: flow-jobs
on:
  pull_request: {}
jobs: { independent: { runs-on: ubuntu-24.04, steps: [{ run: true }] } }
'@
            },
            {
                param($root)
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/aliased-jobs.yml') `
                    -Content @'
name: aliased-jobs
on:
  pull_request: {}
x-job: &shared-job
  runs-on: ubuntu-24.04
  steps:
    - run: true
jobs:
  <<: *shared-job
'@
            },
            {
                param($root)
                $tab = [char]9
                Write-Utf8File `
                    -Path (Join-Path $root '.github/workflows/tab-indented-job.yml') `
                    -Content (@'
name: tab-indented-job
on:
  pull_request: {}
jobs:
  independent:
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@ + "`n${tab}`"required\u002dfinal`":`n${tab}  runs-on: ubuntu-24.04`n")
            })) {
            $fixture = New-AuthorizationFixture -MutateTemplate $mutation
            $fixture = Complete-Candidate -Fixture $fixture
            $results.Add((Invoke-Validation `
                -Fixture $fixture `
                -Base $fixture.Authorization `
                -Candidate $fixture.Candidate))
        }
        $describeInvalidWorkflow = New-TemplateCandidate -Fixture (New-BaseFixture)
        Invoke-Git `
            -Root $describeInvalidWorkflow.Root `
            -Arguments @('checkout', '--quiet', $describeInvalidWorkflow.Template)
        Write-Utf8File `
            -Path (Join-Path $describeInvalidWorkflow.Root '.github/workflows/describe-invalid.yml') `
            -Content @'
name: describe-invalid
on:
  pull_request: {}
jobs:
  "required\u002dfinal":
    runs-on: ubuntu-24.04
    steps:
      - run: true
'@
        Write-CloudFixtureBaseline -Root $describeInvalidWorkflow.Root
        Invoke-Git -Root $describeInvalidWorkflow.Root -Arguments @('add', '--all')
        Invoke-Git `
            -Root $describeInvalidWorkflow.Root `
            -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $describeInvalidWorkflow.Template = Invoke-Git `
            -Root $describeInvalidWorkflow.Root `
            -Arguments @('rev-parse', 'HEAD') `
            -Capture
        $results.Add((Invoke-DescribeResult `
            -Fixture $describeInvalidWorkflow `
            -RuleIdsCsv 'CLOUD-TEST-GOV-001B'))
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'other-workflow reserved identity, strict YAML grammar, proof literals and Describe closure'
    }

    $ordinaryTrustChange = New-TrustTemplateFixture
    Assert-Rejected -Name 'trust upgrade ID is raw-singleton and validator-only' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($ruleIds in @(
            'CLOUD-ARCH-001',
            'CLOUD-BASELINE-TRUST-UPGRADE-001,CLOUD-ARCH-001',
            'CLOUD-BASELINE-TRUST-UPGRADE-001,CLOUD-BASELINE-TRUST-UPGRADE-001')) {
            $results.Add((Invoke-DescribeResult `
                -Fixture $ordinaryTrustChange `
                -RuleIdsCsv $ruleIds))
        }
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'trust upgrade singleton/validator-only identity'
    }

    $mixedTrustChange = New-TrustTemplateFixture
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('checkout', '--quiet', $mixedTrustChange.Template)
    Write-Utf8File -Path (Join-Path $mixedTrustChange.Root 'src/App/Mixed.cs') -Content "internal sealed class Mixed { }`n"
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('add', '--all')
    Invoke-Git -Root $mixedTrustChange.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
    $mixedTrustChange.Template = Invoke-Git -Root $mixedTrustChange.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'trust upgrade cannot mix ordinary paths' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $mixedTrustChange `
            -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'
    }

    Assert-Rejected -Name 'v1 receipt cannot change base-owned wrapper schema or self-test' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()
        foreach ($proofAsset in @(
            $TrustedWrapperRelativePath,
            'scripts/governance/migrations/cloud-baseline-migration-receipt.schema.json',
            'scripts/governance/migrations/TestCloudBaselineMigrationValidator.v1.ps1')) {
            $fixture = New-BaseFixture
            Invoke-Git -Root $fixture.Root -Arguments @('checkout', '--quiet', $fixture.Base)
            [IO.File]::AppendAllText(
                (Join-Path $fixture.Root $proofAsset),
                "`n# forbidden proof-asset candidate change`n",
                [Text.UTF8Encoding]::new($false))
            $fixture | Add-Member `
                -NotePropertyName Template `
                -NotePropertyValue (Commit-All -Root $fixture.Root -Message 'candidate proof asset replacement') `
                -Force
            $results.Add((Invoke-DescribeResult `
                -Fixture $fixture `
                -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'))
        }
        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'base-owned wrapper/schema/self-test immutability'
    }

    Assert-Rejected -Name 'validator-only trust upgrade requires existing mode-100644 validator' -ExpectedCode 'TRUST' -Action {
        $results = [Collections.Generic.List[object]]::new()

        $deletedValidator = New-TrustTemplateFixture
        Invoke-Git -Root $deletedValidator.Root -Arguments @('checkout', '--quiet', $deletedValidator.Template)
        Invoke-Git -Root $deletedValidator.Root -Arguments @(
            'rm', '--quiet', '--', 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1')
        Invoke-Git -Root $deletedValidator.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $deletedValidator.Template = Invoke-Git -Root $deletedValidator.Root -Arguments @('rev-parse', 'HEAD') -Capture
        $results.Add((Invoke-DescribeResult `
            -Fixture $deletedValidator `
            -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'))

        $executableValidator = New-TrustTemplateFixture
        Invoke-Git -Root $executableValidator.Root -Arguments @('checkout', '--quiet', $executableValidator.Template)
        Invoke-Git -Root $executableValidator.Root -Arguments @(
            'update-index', '--chmod=+x', '--', 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1')
        Invoke-Git -Root $executableValidator.Root -Arguments @('commit', '--quiet', '--amend', '--no-edit')
        $executableValidator.Template = Invoke-Git -Root $executableValidator.Root -Arguments @('rev-parse', 'HEAD') -Capture
        $results.Add((Invoke-DescribeResult `
            -Fixture $executableValidator `
            -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'))

        Get-AggregateRejectionResult `
            -Results $results.ToArray() `
            -ExpectedCode 'TRUST' `
            -Context 'validator deletion/executable-mode rejection'
    }

    $addedValidator = New-BaseFixture
    Invoke-Git -Root $addedValidator.Root -Arguments @('checkout', '--quiet', $addedValidator.Base)
    Invoke-Git -Root $addedValidator.Root -Arguments @(
        'rm', '--quiet', '--', 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1')
    $addedValidator.Base = Commit-All -Root $addedValidator.Root -Message 'base without migration validator'
    [IO.File]::Copy(
        $ValidatorPath,
        (Join-Path $addedValidator.Root 'scripts/governance/migrations/ValidateCloudBaselineMigration.v1.ps1'),
        $true)
    $addedValidator | Add-Member `
        -NotePropertyName Template `
        -NotePropertyValue (Commit-All -Root $addedValidator.Root -Message 'candidate adds migration validator') `
        -Force
    Assert-Rejected -Name 'trust upgrade cannot bootstrap a missing validator' -ExpectedCode 'TRUST' -Action {
        Invoke-DescribeResult `
            -Fixture $addedValidator `
            -RuleIdsCsv 'CLOUD-BASELINE-TRUST-UPGRADE-001'
    }

    $trustUpgrade = New-TrustUpgradeFixture
    Assert-Pass -Name 'isolated trust upgrade authorization' -ExpectedText 'authorization recorded' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Base -Candidate $trustUpgrade.Authorization
    }
    $trustUpgrade = Complete-Candidate -Fixture $trustUpgrade
    Assert-Pass -Name 'isolated trust upgrade consumption' -ExpectedText 'receipt consumed' -Action {
        Invoke-Validation -Fixture $trustUpgrade -Base $trustUpgrade.Authorization -Candidate $trustUpgrade.Candidate
    }

    $cancelled = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Assert-Pass -Name 'pending receipt can be cancelled byte-for-byte' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $cancelled -Base $cancelled.Authorization -Candidate $cancelled.Candidate
    }

    $expiredCancellation = New-AuthorizationFixture -MutateReceipt {
        param($json)
        $receipt = ConvertFrom-TestJson -Json $json
        $receipt.issuedAtUtc = $Now.AddDays(-3).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $receipt.expiresAtUtc = $Now.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ssZ')
        return $receipt | ConvertTo-Json -Depth 100
    }
    $expiredCancellation = Complete-Cancellation -Fixture $expiredCancellation
    Assert-Pass -Name 'expired pending receipt can be cancelled for recovery' -ExpectedText 'receipt cancelled' -Action {
        Invoke-Validation -Fixture $expiredCancellation -Base $expiredCancellation.Authorization -Candidate $expiredCancellation.Candidate
    }

    $alteredCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AlterCancelled
    Assert-Rejected -Name 'altered cancelled receipt is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $alteredCancellation -Base $alteredCancellation.Authorization -Candidate $alteredCancellation.Candidate
    }

    $noisyCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -AddExtraPath
    Assert-Rejected -Name 'cancellation cannot carry another path' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation -Fixture $noisyCancellation -Base $noisyCancellation.Authorization -Candidate $noisyCancellation.Candidate
    }

    $executableCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture) -ExecutableMode
    Assert-Rejected -Name 'cancelled receipt executable mode is rejected' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation -Fixture $executableCancellation -Base $executableCancellation.Authorization -Candidate $executableCancellation.Candidate
    }

    $cancelReplay = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $cancelReplay.Root -Arguments @('checkout', '--quiet', $cancelReplay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $cancelReplay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $cancelReplay.Root $CancelledRelativePath),
        (Join-Path $cancelReplay.Root $ReceiptRelativePath),
        $true)
    $cancelReplayAttempt = Commit-All -Root $cancelReplay.Root -Message 'attempt cancelled replay'
    Assert-Rejected -Name 'cancelled migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $cancelReplay -Base $cancelReplay.Candidate -Candidate $cancelReplayAttempt
    }

    $nonDirectAuthorization = New-AuthorizationFixture
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'checkout', '--quiet', $nonDirectAuthorization.Authorization)
    Invoke-Git -Root $nonDirectAuthorization.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'authorization descendant')
    $authorizationDescendant = Invoke-Git `
        -Root $nonDirectAuthorization.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'authorization must be a direct single-parent commit' -ExpectedCode 'AUTHORIZATION' -Action {
        Invoke-Validation `
            -Fixture $nonDirectAuthorization `
            -Base $nonDirectAuthorization.Base `
            -Candidate $authorizationDescendant
    }

    $nonDirectConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @('checkout', '--quiet', $nonDirectConsumption.Candidate)
    Invoke-Git -Root $nonDirectConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'consumption descendant')
    $consumptionDescendant = Invoke-Git `
        -Root $nonDirectConsumption.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'consumption must be a direct single-parent commit' -ExpectedCode 'CONSUME' -Action {
        Invoke-Validation `
            -Fixture $nonDirectConsumption `
            -Base $nonDirectConsumption.Authorization `
            -Candidate $consumptionDescendant
    }

    $mergeConsumption = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', '-b', 'side-parent', $mergeConsumption.Authorization)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'side parent')
    $sideParent = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'checkout', '--quiet', $mergeConsumption.Candidate)
    Invoke-Git -Root $mergeConsumption.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'synthetic merge shape', $sideParent)
    $mergeCandidate = Invoke-Git -Root $mergeConsumption.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'merge-shaped consumption range is rejected' -ExpectedCode 'HISTORY' -Action {
        Invoke-Validation `
            -Fixture $mergeConsumption `
            -Base $mergeConsumption.Authorization `
            -Candidate $mergeCandidate
    }

    $nonDirectCancellation = Complete-Cancellation -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @('checkout', '--quiet', $nonDirectCancellation.Candidate)
    Invoke-Git -Root $nonDirectCancellation.Root -Arguments @(
        'commit', '--quiet', '--allow-empty', '-m', 'cancellation descendant')
    $cancellationDescendant = Invoke-Git `
        -Root $nonDirectCancellation.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Assert-Rejected -Name 'cancellation must be a direct single-parent commit' -ExpectedCode 'CANCEL' -Action {
        Invoke-Validation `
            -Fixture $nonDirectCancellation `
            -Base $nonDirectCancellation.Authorization `
            -Candidate $cancellationDescendant
    }

    $replay = Complete-Candidate -Fixture (New-AuthorizationFixture)
    Invoke-Git -Root $replay.Root -Arguments @('checkout', '--quiet', $replay.Candidate)
    [IO.Directory]::CreateDirectory((Split-Path (Join-Path $replay.Root $ReceiptRelativePath) -Parent)) | Out-Null
    [IO.File]::Copy(
        (Join-Path $replay.Root $ConsumedRelativePath),
        (Join-Path $replay.Root $ReceiptRelativePath),
        $true)
    $replayAttempt = Commit-All -Root $replay.Root -Message 'attempt replay'
    Assert-Rejected -Name 'consumed migration ID cannot replay' -ExpectedCode 'REPLAY' -Action {
        Invoke-Validation -Fixture $replay -Base $replay.Candidate -Candidate $replayAttempt
    }

    $zero = New-BaseFixture
    Assert-Rejected -Name 'zero trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base '0000000000000000000000000000000000000000' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'short trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base $zero.Base.Substring(0, 12) -Candidate $zero.Base
    }

    Assert-Rejected -Name 'symbolic trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $zero -Base 'HEAD' -Candidate $zero.Base
    }

    Assert-Rejected -Name 'unexpected positional argument rejected' -ExpectedCode 'PARAMETER' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $zero.Root,
            '-TrustedBaseRevision', $zero.Base,
            '-CandidateRevision', $zero.Base,
            'unexpected')
    }

    $candidateNotHead = New-BaseFixture
    Write-Utf8File -Path (Join-Path $candidateNotHead.Root 'docs/later.md') -Content "later`n"
    $laterHead = Commit-All -Root $candidateNotHead.Root -Message 'later head'
    Assert-Rejected -Name 'candidate revision must equal checked-out HEAD' -ExpectedCode 'REVISION' -Action {
        Invoke-PowerShellResult -Arguments @(
            '-File', $TrustedWrapperPath,
            '-RepositoryRoot', $candidateNotHead.Root,
            '-TrustedBaseRevision', $candidateNotHead.Base,
            '-CandidateRevision', $candidateNotHead.Base)
    }

    $analyzerBypass = New-BaseFixture
    Write-Utf8File -Path (Join-Path $analyzerBypass.Root 'src/Analyzers/Bypass.cs') -Content "internal sealed class Bypass { }`n"
    $analyzerCandidate = Commit-All -Root $analyzerBypass.Root -Message 'attempt analyzer bypass'
    Assert-Rejected -Name 'analyzer source cannot change without receipt' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $analyzerBypass -Base $analyzerBypass.Base -Candidate $analyzerCandidate
    }

    $wrapperBypass = New-BaseFixture
    Write-Utf8File `
        -Path (Join-Path $wrapperBypass.Root 'scripts/governance/migrations/InvokeCloudBaselineMigrationFromTrustedBase.v1.ps1') `
        -Content "exit 0`n"
    $wrapperCandidate = Commit-All -Root $wrapperBypass.Root -Message 'attempt wrapper bypass'
    Assert-Rejected -Name 'base-extracted wrapper ignores candidate wrapper bypass' -ExpectedCode 'IMMUTABLE' -Action {
        Invoke-Validation -Fixture $wrapperBypass -Base $wrapperBypass.Base -Candidate $wrapperCandidate
    }

    $releaseAnchor = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseAnchor.Root 'docs/main-ahead.md') -Content "main ahead`n"
    $trustedMain = Commit-All -Root $releaseAnchor.Root -Message 'unprotected main advance'
    Assert-Pass -Name 'release anchor allows root endpoint and unprotected main advance' -ExpectedText 'trusted release anchor passed' -Action {
        Invoke-Validation `
            -Fixture $releaseAnchor `
            -Base $trustedMain `
            -Candidate $releaseAnchor.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $releaseProtected = New-BaseFixture
    Write-Utf8File -Path (Join-Path $releaseProtected.Root 'src/tests/Sample.Tests/NewCase.cs') -Content "internal sealed class NewCase { }`n"
    $protectedMain = Commit-All -Root $releaseProtected.Root -Message 'protected main advance'
    Assert-Rejected -Name 'release anchor rejects protected drift' -ExpectedCode 'RELEASE' -Action {
        Invoke-Validation `
            -Fixture $releaseProtected `
            -Base $protectedMain `
            -Candidate $releaseProtected.Base `
            -Relationship 'HeadAncestorOfBase'
    }

    $wrongReleaseDirection = New-TemplateCandidate -Fixture (New-BaseFixture)
    $sideRelease = New-BaseFixture
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', '-b', 'release-side', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/side-release.md') -Content "side`n"
    $sideReleaseCandidate = Commit-All -Root $sideRelease.Root -Message 'side release candidate'
    Invoke-Git -Root $sideRelease.Root -Arguments @('checkout', '--quiet', $sideRelease.Base)
    Write-Utf8File -Path (Join-Path $sideRelease.Root 'docs/main-first-parent.md') -Content "main`n"
    [void](Commit-All -Root $sideRelease.Root -Message 'main first-parent advance')
    Invoke-Git -Root $sideRelease.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'merge side release', $sideReleaseCandidate)
    $sideReleaseTrustedMain = Invoke-Git -Root $sideRelease.Root -Arguments @('rev-parse', 'HEAD') -Capture
    Assert-Rejected -Name 'release anchor rejects invalid ancestry direction or first-parent placement' -ExpectedCode 'ANCESTRY' -Action {
        $wrongDirectionResult = Invoke-Validation `
            -Fixture $wrongReleaseDirection `
            -Base $wrongReleaseDirection.Base `
            -Candidate $wrongReleaseDirection.Template `
            -Relationship 'HeadAncestorOfBase'
        $sidePlacementResult = Invoke-Validation `
            -Fixture $sideRelease `
            -Base $sideReleaseTrustedMain `
            -Candidate $sideReleaseCandidate `
            -Relationship 'HeadAncestorOfBase'
        Get-AggregateRejectionResult `
            -Results @($wrongDirectionResult, $sidePlacementResult) `
            -ExpectedCode 'ANCESTRY' `
            -Context 'release ancestry guards'
    }

    $mergeRange = New-BaseFixture
    $mergeRangeBase = $mergeRange.Base
    Invoke-Git -Root $mergeRange.Root -Arguments @(
        'checkout', '--quiet', '-b', 'range-side', $mergeRangeBase)
    Write-Utf8File -Path (Join-Path $mergeRange.Root 'docs/range-side.md') -Content "side`n"
    $mergeRangeSide = Commit-All -Root $mergeRange.Root -Message 'range side'
    Invoke-Git -Root $mergeRange.Root -Arguments @(
        'checkout', '--quiet', '-b', 'range-main', $mergeRangeBase)
    Write-Utf8File -Path (Join-Path $mergeRange.Root 'docs/range-main.md') -Content "main`n"
    [void](Commit-All -Root $mergeRange.Root -Message 'range main')
    Invoke-Git -Root $mergeRange.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'merge range side', $mergeRangeSide)
    $mergeRangeHead = Invoke-Git `
        -Root $mergeRange.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    $mergeRange | Add-Member -NotePropertyName Template -NotePropertyValue $mergeRangeHead

    $releaseEndpointMerge = New-BaseFixture
    $releaseEndpointBase = $releaseEndpointMerge.Base
    Invoke-Git -Root $releaseEndpointMerge.Root -Arguments @(
        'checkout', '--quiet', '-b', 'endpoint-side', $releaseEndpointBase)
    Write-Utf8File -Path (Join-Path $releaseEndpointMerge.Root 'docs/endpoint-side.md') -Content "side`n"
    $releaseEndpointSide = Commit-All -Root $releaseEndpointMerge.Root -Message 'endpoint side'
    Invoke-Git -Root $releaseEndpointMerge.Root -Arguments @(
        'checkout', '--quiet', '-b', 'endpoint-main', $releaseEndpointBase)
    Write-Utf8File -Path (Join-Path $releaseEndpointMerge.Root 'docs/endpoint-main.md') -Content "main`n"
    [void](Commit-All -Root $releaseEndpointMerge.Root -Message 'endpoint main')
    Invoke-Git -Root $releaseEndpointMerge.Root -Arguments @(
        'merge', '--quiet', '--no-ff', '-m', 'untrusted endpoint merge', $releaseEndpointSide)
    $releaseMergeCandidate = Invoke-Git `
        -Root $releaseEndpointMerge.Root `
        -Arguments @('rev-parse', 'HEAD') `
        -Capture
    Write-Utf8File `
        -Path (Join-Path $releaseEndpointMerge.Root 'docs/trusted-after-endpoint.md') `
        -Content "trusted`n"
    $trustedAfterEndpointMerge = Commit-All `
        -Root $releaseEndpointMerge.Root `
        -Message 'trusted main after endpoint merge'

    Assert-Rejected -Name 'Describe normal and release ranges reject merge commits' -ExpectedCode 'HISTORY' -Action {
        $describeMergeResult = Invoke-DescribeResult `
            -Fixture $mergeRange `
            -RuleIdsCsv 'CLOUD-TEST-GOV-001B'
        $normalMergeResult = Invoke-Validation `
            -Fixture $mergeRange `
            -Base $mergeRangeBase `
            -Candidate $mergeRangeHead
        $releaseMergeResult = Invoke-Validation `
            -Fixture $mergeRange `
            -Base $mergeRangeHead `
            -Candidate $mergeRangeBase `
            -Relationship 'HeadAncestorOfBase'
        $releaseEndpointMergeResult = Invoke-Validation `
            -Fixture $releaseEndpointMerge `
            -Base $trustedAfterEndpointMerge `
            -Candidate $releaseMergeCandidate `
            -Relationship 'HeadAncestorOfBase'
        Get-AggregateRejectionResult `
            -Results @(
                $describeMergeResult,
                $normalMergeResult,
                $releaseMergeResult,
                $releaseEndpointMergeResult) `
            -ExpectedCode 'HISTORY' `
            -Context 'merge-free history ranges and untrusted release ancestor endpoints'
    }

    $orphan = New-BaseFixture
    Invoke-Git -Root $orphan.Root -Arguments @('checkout', '--quiet', '--orphan', 'orphan-line')
    Invoke-Git -Root $orphan.Root -Arguments @('rm', '--quiet', '-f', '-r', '--ignore-unmatch', '.')
    Write-Utf8File -Path (Join-Path $orphan.Root 'orphan.txt') -Content "orphan`n"
    $orphanCandidate = Commit-All -Root $orphan.Root -Message 'orphan commit'
    Assert-Rejected -Name 'same-repository orphan history rejected' -ExpectedCode 'ANCESTRY' -Action {
        Invoke-Validation -Fixture $orphan -Base $orphan.Base -Candidate $orphanCandidate
    }

    $unrelatedLeft = New-BaseFixture
    $unrelatedRight = New-BaseFixture
    Write-Utf8File -Path (Join-Path $unrelatedRight.Root 'src/App/Unrelated.cs') -Content "internal sealed class Unrelated { }`n"
    $unrelatedRight.Base = Commit-All -Root $unrelatedRight.Root -Message 'unrelated history'
    Assert-Rejected -Name 'unrelated trusted revision rejected' -ExpectedCode 'REVISION' -Action {
        Invoke-Validation -Fixture $unrelatedLeft -Base $unrelatedRight.Base -Candidate $unrelatedLeft.Base
    }
}
finally {
    foreach ($root in $script:TempRoots) {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failed -ne 0 -or $script:Passed -ne $script:ExpectedTestCount) {
    throw "Cloud baseline migration validator self-tests failed: expected=$($script:ExpectedTestCount) passed=$($script:Passed) failed=$($script:Failed)."
}

Write-Host "Cloud baseline migration validator self-tests passed: $($script:Passed)/$($script:ExpectedTestCount)."
$global:LASTEXITCODE = 0
exit 0
