[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

$ruleId = 'CLOUD-TEST-GOV-001'
$policyPath = Join-Path $RepositoryRoot 'scripts/tests/TestCloudTestGovernancePolicy.ps1'
$reviewedBaselinePath = Join-Path $RepositoryRoot 'scripts/tests/baselines/cloud-test-governance.baseline.json'
$reviewedWaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/cloud-test-governance.waivers.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "cloud-test-governance-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $tempRoot -ItemType Directory -Force)
$script:acceptedFixtureCount = 0
$script:rejectedFixtureCount = 0

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $directory = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [void](New-Item $directory -ItemType Directory -Force)
    }
    [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    Write-Utf8File -Path $Path -Value "$(($Value | ConvertTo-Json -Depth 100))`n"
}

function Get-FixtureHash {
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function New-Traits {
    param(
        [string]$TestKind,
        [string]$Runtime,
        [string]$Risk,
        [string]$Capability,
        [string]$Owner,
        [string]$RegressionId
    )

    $traits = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($TestKind)) { $traits.TestKind = @($TestKind) }
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) { $traits.Runtime = @($Runtime) }
    if (-not [string]::IsNullOrWhiteSpace($Risk)) { $traits.Risk = @($Risk) }
    if (-not [string]::IsNullOrWhiteSpace($Capability)) { $traits.Capability = @($Capability) }
    if (-not [string]::IsNullOrWhiteSpace($Owner)) { $traits.Owner = @($Owner) }
    if (-not [string]::IsNullOrWhiteSpace($RegressionId)) { $traits.RegressionId = @($RegressionId) }
    return [pscustomobject]$traits
}

function New-TestRecord {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$TypeName = 'Fixture.Tests.SampleTests',
        [string]$MethodName = 'Existing',
        [ValidateSet('Fact', 'Theory')][string]$AttributeCategory = 'Fact',
        [string]$TestAttributeType = 'Xunit.FactAttribute',
        [int]$InlineDataRows = 0,
        [string[]]$InlineDataSignatures = @(),
        [string[]]$ExecutionTypeNames = @(),
        [AllowNull()][object]$Traits = $null,
        [bool]$Disabled = $false
    )

    if ($null -eq $Traits) { $Traits = [pscustomobject]@{} }
    if ($ExecutionTypeNames.Count -eq 0) { $ExecutionTypeNames = @($TypeName) }
    if ($InlineDataRows -gt 0 -and $InlineDataSignatures.Count -eq 0) {
        $InlineDataSignatures = [string[]]@(1..$InlineDataRows | ForEach-Object { "cloud-cad-v1:$(Get-FixtureHash "fixture-inline-row-$_")" })
    }

    $physicalId = "cloud-test-physical-v1:$(Get-FixtureHash "physical|$Id")"
    $logicalId = "cloud-test-decl-v1:$(Get-FixtureHash "logical|$Id")"
    $executionTypes = @($ExecutionTypeNames | ForEach-Object {
        [pscustomobject][ordered]@{
            id = "cloud-test-execution-v1:$(Get-FixtureHash "execution|$Id|$_")"
            name = $_
            traits = $Traits
        }
    })
    $projectedCases = if ($AttributeCategory -eq 'Theory' -and $InlineDataRows -gt 0) {
        $InlineDataRows * $executionTypes.Count
    }
    else {
        $executionTypes.Count
    }

    return [pscustomobject][ordered]@{
        id = $physicalId
        logicalId = $logicalId
        symbol = "$TypeName.$MethodName()"
        executionType = $TypeName
        declaringType = $TypeName
        methodName = $MethodName
        parameterSignature = ''
        attributeCategory = $AttributeCategory
        testAttributeType = $TestAttributeType
        testAttributePolicy = [pscustomobject][ordered]@{
            signature = "Skip=$(if ($Disabled) { 'disabled' } else { '' })|Explicit=False|SkipWhen=|SkipUnless=|SkipType=|SkipExceptions=|Timeout=0|DataPolicies="
            isDisabled = $Disabled
            skip = if ($Disabled) { 'disabled' } else { '' }
            explicit = $false
            skipWhen = ''
            skipUnless = ''
            skipType = ''
            skipExceptions = ''
            timeout = 0
        }
        inlineDataRows = $InlineDataRows
        inlineDataSignatures = [string[]]$InlineDataSignatures
        dynamicDataSources = [string[]]@()
        executionTypes = [object[]]$executionTypes
        projectedCases = $projectedCases
        traits = $Traits
    }
}

function New-Baseline {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests
    )

    $executionTemplates = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
    }
    $projectedCases = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | Measure-Object -Property projectedCases -Sum).Sum)
    }

    return [pscustomobject][ordered]@{
        schemaVersion = '1.2'
        ruleId = $ruleId
        scanner = [pscustomobject][ordered]@{
            engine = 'behavior-fixture'
            activeDotnetSdk = 'fixture'
            metadataLoadContextSha256 = ('0' * 64)
        }
        # Synthetic snapshots intentionally reuse the reviewed registry instead of
        # maintaining a second list that could drift from the policy under test.
        allowedMetadata = ($script:reviewedAllowedMetadata | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20)
        projects = @([pscustomobject][ordered]@{
            projectPath = $ProjectPath
            projectName = $ProjectName
            isLegacy = $false
            freezeMode = 'None'
            frozenTypePatterns = [string[]]@()
            frozenSourceFiles = [string[]]@()
            allowedNewTestKinds = [string[]]@()
            allowedNewRuntimes = [string[]]@()
            forbiddenNewTestKinds = [string[]]@()
            discoveryCeilings = [object[]]@()
            protectBaselineRemovals = $true
            baselineDeclarations = $Tests.Count
            baselineExecutionTemplates = $executionTemplates
            baselineProjectedCases = $projectedCases
            tests = [object[]]$Tests
        })
    }
}

function New-Snapshot {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests
    )

    $executionTemplates = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
    }
    $projectedCases = if ($Tests.Count -eq 0) { 0 } else {
        [int](($Tests | Measure-Object -Property projectedCases -Sum).Sum)
    }

    return [pscustomobject][ordered]@{
        projectPath = $ProjectPath
        projectName = $ProjectName
        assemblyPath = 'fixture/Fixture.Tests.dll'
        assemblySha256 = ('1' * 64)
        declarations = $Tests.Count
        executionTemplates = $executionTemplates
        projectedCases = $projectedCases
        tests = [object[]]$Tests
    }
}

function New-WaiverManifest {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Waivers)

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        ruleId = $ruleId
        waivers = [object[]]$Waivers
    }
}

function Invoke-SnapshotValidation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers
    )

    $baselinePath = Join-Path $tempRoot "$Name.baseline.json"
    $snapshotPath = Join-Path $tempRoot "$Name.snapshot.json"
    $waiverPath = Join-Path $tempRoot "$Name.waivers.json"
    Write-JsonFile -Value $Baseline -Path $baselinePath
    Write-JsonFile -Value $Snapshot -Path $snapshotPath
    Write-JsonFile -Value $Waivers -Path $waiverPath

    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateSnapshot `
        -RepositoryRoot $RepositoryRoot `
        -BaselinePath $baselinePath `
        -WaiverPath $waiverPath `
        -CurrentSnapshotPath $snapshotPath 2>&1

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-Accepted {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers
    )

    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -Waivers $Waivers
    if ($result.ExitCode -ne 0) {
        throw "Fixture '$Name' should pass:`n$($result.Output)"
    }
    Write-Host "Accepted Cloud test-governance fixture: $Name"
    $script:acceptedFixtureCount++
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$Waivers,
        [Parameter(Mandatory)][string]$ExpectedCode
    )

    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -Waivers $Waivers
    if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
        throw "Fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
    }
    Write-Host "Rejected Cloud test-governance fixture: $Name ($ExpectedCode)"
    $script:rejectedFixtureCount++
}

function Invoke-StaticValidation {
    param([Parameter(Mandatory)][string]$ValidationRoot)

    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateStatic `
        -RepositoryRoot $ValidationRoot `
        -BaselinePath (Join-Path $ValidationRoot 'scripts/tests/baselines/cloud-test-governance.baseline.json') `
        -WaiverPath (Join-Path $ValidationRoot 'scripts/tests/baselines/cloud-test-governance.waivers.json') `
        -Configuration Release 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-StaticRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][scriptblock]$Mutate,
        [Parameter(Mandatory)][scriptblock]$Restore,
        [Parameter(Mandatory)][string]$ExpectedCode
    )

    & $Mutate
    try {
        $result = Invoke-StaticValidation -ValidationRoot $ValidationRoot
        if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
            throw "Static fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
        }
        Write-Host "Rejected Cloud static-governance fixture: $Name ($ExpectedCode)"
        $script:rejectedFixtureCount++
    }
    finally {
        & $Restore
    }
}

function Assert-WorkflowSemanticRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][string]$WorkflowPath,
        [Parameter(Mandatory)][scriptblock]$Mutate,
        [Parameter(Mandatory)][scriptblock]$Restore,
        [Parameter(Mandatory)][string]$ExpectedMessage,
        [string]$ExpectedCode = "$ruleId-CI"
    )

    & $Mutate
    try {
        $output = & pwsh -NoLogo -NoProfile -File $policyPath `
            -Mode ValidateWorkflowFixture `
            -RepositoryRoot $ValidationRoot `
            -BaselinePath (Join-Path $ValidationRoot 'scripts/tests/baselines/cloud-test-governance.baseline.json') `
            -WaiverPath (Join-Path $ValidationRoot 'scripts/tests/baselines/cloud-test-governance.waivers.json') `
            -WorkflowFixturePath $WorkflowPath 2>&1
        $text = ($output | Out-String).Trim()
        $plainText = [regex]::Replace($text, "`e\[[0-9;?]*[ -/]*[@-~]", '')
        # PowerShell's formatted ErrorRecord inserts visual `|` column markers
        # when it wraps nested errors. Remove those presentation separators so
        # semantic-message assertions operate on the original policy message.
        $plainText = [regex]::Replace($plainText, '\s*\|\s*', ' ')
        $normalizedText = [regex]::Replace($plainText, '\s+', ' ')
        $normalizedExpectedMessage = [regex]::Replace($ExpectedMessage, '\s+', ' ')
        if ($LASTEXITCODE -eq 0 -or
            -not $normalizedText.Contains($ExpectedCode, [StringComparison]::Ordinal) -or
            -not $normalizedText.Contains($normalizedExpectedMessage, [StringComparison]::Ordinal)) {
            throw "Workflow semantic fixture '$Name' should fail with '$ExpectedMessage'; exit=${LASTEXITCODE}:`n$text"
        }
        Write-Host "Rejected Cloud workflow-semantic fixture: $Name ($ExpectedCode)"
        $script:rejectedFixtureCount++
    }
    finally {
        & $Restore
    }
}

function Copy-RepositoryFixture {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$DestinationRoot
    )

    $excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.git', '.vs', '.idea', 'bin', 'obj', 'node_modules', 'TestResults', 'artifacts')) {
        [void]$excludedDirectoryNames.Add($name)
    }

    function Copy-DirectoryContent {
        param([string]$Source, [string]$Destination)

        [void](New-Item $Destination -ItemType Directory -Force)
        foreach ($entry in Get-ChildItem $Source -Force) {
            if ($entry.PSIsContainer) {
                if (-not $excludedDirectoryNames.Contains($entry.Name)) {
                    Copy-DirectoryContent -Source $entry.FullName -Destination (Join-Path $Destination $entry.Name)
                }
                continue
            }
            [System.IO.File]::Copy($entry.FullName, (Join-Path $Destination $entry.Name), $true)
        }
    }

    Copy-DirectoryContent -Source $SourceRoot -Destination $DestinationRoot
}

try {
    foreach ($requiredPath in @($policyPath, $reviewedBaselinePath, $reviewedWaiverPath)) {
        if (-not (Test-Path $requiredPath -PathType Leaf)) {
            throw "Required Cloud test-governance asset is missing: $requiredPath"
        }
    }

    $reviewedBaseline = Get-Content $reviewedBaselinePath -Raw | ConvertFrom-Json -Depth 100
    if ($reviewedBaseline.ruleId -ne $ruleId -or @($reviewedBaseline.projects).Count -ne 3) {
        throw 'Reviewed Cloud baseline must use CLOUD-TEST-GOV-001 and contain exactly three backend test projects.'
    }
    $reviewedProjectedCases = [int](($reviewedBaseline.projects | Measure-Object -Property baselineProjectedCases -Sum).Sum)
    if ($reviewedProjectedCases -ne 591) {
        throw "Reviewed Cloud backend baseline must contain 591 projected runner cases; found $reviewedProjectedCases."
    }
    $script:reviewedAllowedMetadata = $reviewedBaseline.allowedMetadata
    $reviewedInlineDataSignatures = @($reviewedBaseline.projects |
        ForEach-Object { $_.tests } |
        ForEach-Object { $_.inlineDataSignatures })
    if ($reviewedInlineDataSignatures.Count -ne 81 -or
        @($reviewedInlineDataSignatures | Where-Object { [string]$_ -notmatch '^cloud-cad-v1:[0-9a-f]{64}$' }).Count -gt 0 -or
        @($reviewedInlineDataSignatures | Sort-Object -Unique).Count -le 1) {
        throw 'Reviewed Cloud baseline must preserve 81 canonical typed InlineData payload signatures with real value diversity.'
    }

    $attributeFixturePath = Join-Path $tempRoot 'CloudTestAttributePayloadFixture.dll'
    Add-Type -Language CSharp -OutputType Library -OutputAssembly $attributeFixturePath -TypeDefinition @'
#nullable enable
using System;

namespace CloudGovernance;

public enum PayloadKind { Zero = 0, One = 1 }

[AttributeUsage(AttributeTargets.Method)]
public sealed class PayloadAttribute : Attribute
{
    public PayloadAttribute(string? text, int number, PayloadKind kind, Type type, int[] values) { }
    public string? Name { get; set; }
    public bool Flag;
}

public static class AttributePayloadFixture
{
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void Baseline() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void Identical() { }
    [Payload("beta", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void StringChanged() { }
    [Payload(null, 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void NullChanged() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 3 }, Name = "named", Flag = true)] public static void ArrayValueChanged() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 2, 1 }, Name = "named", Flag = true)] public static void ArrayOrderChanged() { }
    [Payload("alpha", 7, PayloadKind.One, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void EnumChanged() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(int), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void TypeChanged() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "other", Flag = true)] public static void NamedPropertyChanged() { }
    [Payload("alpha", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = false)] public static void NamedFieldChanged() { }
    [Payload("\u00e9", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void ComposedUnicode() { }
    [Payload("e\u0301", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void DecomposedUnicode() { }
    [Payload("", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void EmptyString() { }
    [Payload("\0", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void EmbeddedNull() { }
    [Payload("\U0001f600", 7, PayloadKind.Zero, typeof(string), new int[] { 1, 2 }, Name = "named", Flag = true)] public static void SupplementaryPlane() { }
}
'@
    $attributeCodecOutput = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateAttributePayloadFixture `
        -RepositoryRoot $RepositoryRoot `
        -AttributeFixtureAssemblyPath $attributeFixturePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Typed CustomAttributeData codec fixture should pass:`n$(($attributeCodecOutput | Out-String).Trim())"
    }
    Write-Host 'Accepted Cloud test-governance fixture: typed-custom-attribute-payload-codec'
    $script:acceptedFixtureCount++

    $runnerIdentityOutput = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateRunnerIdentityFixture `
        -RepositoryRoot $RepositoryRoot 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Ordinal runner identity fixture should pass:`n$(($runnerIdentityOutput | Out-String).Trim())"
    }
    Write-Host 'Accepted Cloud test-governance fixture: ordinal-runner-identity-codec'
    $script:acceptedFixtureCount++

    $currentStatic = Invoke-StaticValidation -ValidationRoot $RepositoryRoot
    if ($currentStatic.ExitCode -ne 0) {
        throw "Current repository static policy should pass:`n$($currentStatic.Output)"
    }
    Write-Host 'Accepted Cloud static-governance fixture: current-repository-static-policy'
    $script:acceptedFixtureCount++

    $projectPath = 'src/tests/Fixture.Tests/Fixture.Tests.csproj'
    $projectName = 'Fixture.Tests'
    $emptyWaivers = New-WaiverManifest -Waivers @()
    $classified = New-Traits -TestKind Unit -Runtime Pure -Risk P1 -Capability TestGovernance -Owner Cloud.Tests
    $existing = New-TestRecord -Id 'existing'
    $newClassified = New-TestRecord -Id 'new-classified' -MethodName 'NewClassified' -Traits $classified

    Assert-Accepted -Name 'unchanged-baseline' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Waivers $emptyWaivers

    Assert-Rejected -Name 'baseline-fact-removal' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-REMOVAL"

    $unclassified = New-TestRecord -Id 'new-unclassified' -MethodName 'NewUnclassified'
    Assert-Rejected -Name 'new-test-without-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $unclassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    Assert-Accepted -Name 'new-test-with-complete-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newClassified)) `
        -Waivers $emptyWaivers

    $executionRouteBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)
    $executionRouteBaseline.projects[0].allowedNewTestKinds = @('Unit')
    $executionRouteBaseline.projects[0].allowedNewRuntimes = @('Pure')
    $executionRouteBaseline.projects[0].forbiddenNewTestKinds = @('EndToEnd')
    $derivedExecutionRouteBypass = New-TestRecord `
        -Id 'new-derived-execution-route-bypass' `
        -TypeName 'Fixture.Tests.BaseTests' `
        -MethodName 'NewDerivedExecutionRouteBypass' `
        -ExecutionTypeNames @('Fixture.Tests.DerivedTests') `
        -Traits $classified
    $derivedExecutionRouteBypass.executionTypes[0].traits = New-Traits `
        -TestKind EndToEnd -Runtime Aspire -Risk P0 -Capability TestGovernance -Owner Cloud.Tests
    Assert-Rejected -Name 'concrete-inherited-execution-must-obey-project-route' `
        -Baseline $executionRouteBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $derivedExecutionRouteBypass)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-ROUTE"

    $lowercaseTraits = New-Traits -TestKind unit -Runtime pure -Risk p1 -Capability testgovernance -Owner cloud.tests
    $lowercaseClassified = New-TestRecord -Id 'new-lowercase-classification' -MethodName 'NewLowercaseClassification' -Traits $lowercaseTraits
    Assert-Rejected -Name 'classification-registry-is-ordinal-and-case-sensitive' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $lowercaseClassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    $wrongCaseTraitKeys = [pscustomobject][ordered]@{
        testkind = @('Unit')
        runtime = @('Pure')
        risk = @('P1')
        capability = @('TestGovernance')
        owner = @('Cloud.Tests')
    }
    $wrongCaseKeyClassified = New-TestRecord -Id 'new-wrong-case-trait-keys' -MethodName 'NewWrongCaseTraitKeys' -Traits $wrongCaseTraitKeys
    Assert-Rejected -Name 'classification-trait-keys-are-ordinal-and-case-sensitive' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $wrongCaseKeyClassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    $classifiedExisting = New-TestRecord -Id 'classified-existing' -MethodName 'ClassifiedExisting' -Traits $classified
    $classificationRemoved = New-TestRecord -Id 'classified-existing' -MethodName 'ClassifiedExisting'
    Assert-Rejected -Name 'existing-test-cannot-silently-remove-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($classifiedExisting)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($classificationRemoved)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CLASSIFICATION"

    $frozenBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)
    $frozenBaseline.projects[0].freezeMode = 'All'
    Assert-Rejected -Name 'fully-frozen-project-cannot-add-classified-test' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newClassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-FROZEN"

    $structureFrozenBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)
    $structureFrozenBaseline.projects[0].freezeMode = 'Structure'
    Assert-Rejected -Name 'structure-frozen-project-cannot-add-classified-test' `
        -Baseline $structureFrozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newClassified)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-FROZEN"

    $theoryFour = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 4
    $theoryThree = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 3 -Traits $classified
    Assert-Rejected -Name 'theory-inline-row-decrease' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFour)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryThree)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-INLINE-DATA"

    $replacementSignatures = @(
        1..3 | ForEach-Object { "cloud-cad-v1:$(Get-FixtureHash "fixture-inline-row-$_")" }
    ) + @("cloud-cad-v1:$(Get-FixtureHash 'fixture-inline-row-replacement')")
    $theoryReplacement = New-TestRecord -Id 'theory' -MethodName 'Rows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 4 -InlineDataSignatures $replacementSignatures -Traits $classified
    Assert-Rejected -Name 'same-count-inline-row-replacement' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFour)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryReplacement)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-INLINE-DATA"

    $duplicateA = "cloud-cad-v1:$(Get-FixtureHash 'duplicate-a')"
    $duplicateB = "cloud-cad-v1:$(Get-FixtureHash 'duplicate-b')"
    $duplicateTheoryBaseline = New-TestRecord -Id 'duplicate-theory' -MethodName 'DuplicateRows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 2 -InlineDataSignatures @($duplicateA, $duplicateA)
    $duplicateTheoryReplacement = New-TestRecord -Id 'duplicate-theory' -MethodName 'DuplicateRows' -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -InlineDataRows 2 -InlineDataSignatures @($duplicateA, $duplicateB) -Traits $classified
    Assert-Rejected -Name 'duplicate-inline-row-multiplicity-cannot-hide-replacement' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($duplicateTheoryBaseline)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($duplicateTheoryReplacement)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-INLINE-DATA"

    $twoExecutions = New-TestRecord -Id 'inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.One', 'Fixture.Tests.Two')
    $oneExecution = New-TestRecord -Id 'inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.One') -Traits $classified
    Assert-Rejected -Name 'inherited-execution-template-decrease' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($twoExecutions)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($oneExecution)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-CASE-DECREASE"

    $disabled = New-TestRecord -Id 'existing' -Traits $classified -Disabled $true
    Assert-Rejected -Name 'required-test-cannot-add-skip' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($disabled)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-DISABLED"

    $unknownCustomFact = New-TestRecord -Id 'unknown-custom-fact' -MethodName 'Custom' -TestAttributeType 'Fixture.Tests.CustomFactAttribute' -Traits $classified
    Assert-Rejected -Name 'unknown-custom-fact-fails-closed' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $unknownCustomFact)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-SCAN"

    $workspaceBaselineProject = @($reviewedBaseline.projects | Where-Object { $_.projectName -eq 'IIoT.EndToEndTests' })
    $workspaceBaselineTest = @($workspaceBaselineProject.tests | Where-Object { $_.testAttributeType -like '*WorkspaceAlignmentFactAttribute' })
    if ($workspaceBaselineProject.Count -ne 1 -or $workspaceBaselineTest.Count -ne 1) {
        throw 'Reviewed Cloud baseline must contain exactly one IIoT.EndToEndTests WorkspaceAlignmentFact declaration.'
    }
    $workspaceSecond = New-TestRecord `
        -Id 'workspace-alignment-second' `
        -TypeName 'IIoT.EndToEndTests.SecondWorkspaceAlignmentTests' `
        -MethodName 'SecondWorkspaceAlignment' `
        -TestAttributeType $workspaceBaselineTest[0].testAttributeType `
        -Traits (New-Traits -TestKind Contract -Runtime LiveExternal -Risk P0 -Capability AiRead -Owner Cloud.AiRead)
    Assert-Rejected -Name 'workspace-alignment-custom-fact-remains-unique' `
        -Baseline (New-Baseline -ProjectPath $workspaceBaselineProject[0].projectPath -ProjectName $workspaceBaselineProject[0].projectName -Tests @($workspaceBaselineTest[0])) `
        -Snapshot (New-Snapshot -ProjectPath $workspaceBaselineProject[0].projectPath -ProjectName $workspaceBaselineProject[0].projectName -Tests @($workspaceBaselineTest[0], $workspaceSecond)) `
        -Waivers $emptyWaivers `
        -ExpectedCode "$ruleId-WORKSPACE-ALIGNMENT"

    $expiredWaiver = [pscustomobject][ordered]@{
        id = 'CLOUD-TEST-GOV-001-W001'
        projectPath = $projectPath
        symbol = $existing.id
        changeKind = 'Remove'
        regressionId = 'CLOUD-REG-001'
        targetProject = 'src/tests/Fixture.TargetTests/Fixture.TargetTests.csproj'
        testKind = 'Unit'
        owner = 'Cloud.Tests'
        reason = 'Expired behavior fixture; must not authorize removal.'
        approvedBy = 'ShuJinHao'
        expiresOn = [DateTime]::UtcNow.AddDays(-1).ToString('yyyy-MM-dd')
    }
    Assert-Rejected -Name 'expired-waiver-cannot-authorize-removal' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -Waivers (New-WaiverManifest -Waivers @($expiredWaiver)) `
        -ExpectedCode "$ruleId-WAIVER"

    $staticRoot = Join-Path $tempRoot 'static-repository'
    Copy-RepositoryFixture -SourceRoot $RepositoryRoot -DestinationRoot $staticRoot

    $codeOwnersPath = Join-Path $staticRoot '.github/CODEOWNERS'
    $codeOwnersOriginal = Get-Content $codeOwnersPath -Raw
    Assert-StaticRejected -Name 'test-method-bodies-require-code-owner' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal.Replace("/src/tests/**/*.cs @ShuJinHao`n", '')
        } `
        -Restore { Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal } `
        -ExpectedCode "$ruleId-CODEOWNER"

    Assert-StaticRejected -Name 'later-code-owner-rule-cannot-shadow-test-ownership' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $codeOwnersPath -Value "$($codeOwnersOriginal.TrimEnd())`n/src/tests/**/*.cs @UnreviewedOwner`n"
        } `
        -Restore { Write-Utf8File -Path $codeOwnersPath -Value $codeOwnersOriginal } `
        -ExpectedCode "$ruleId-CODEOWNER"

    $frozenBodyPath = Join-Path $staticRoot 'src/tests/IIoT.EndToEndTests/ConfigurationGuardTests.cs'
    $frozenBodyOriginal = Get-Content $frozenBodyPath -Raw
    Assert-StaticRejected -Name 'frozen-end-to-end-test-body-cannot-weaken-assertion' -ValidationRoot $staticRoot `
        -Mutate {
            $strongAssertion = @'
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{DesignTimeConnectionStringResolver.ConnectionStringEnvironmentVariable}*");
'@
            if (-not $frozenBodyOriginal.Contains($strongAssertion.Trim(), [StringComparison]::Ordinal)) {
                throw 'Frozen body fixture could not locate the reviewed assertion.'
            }
            Write-Utf8File -Path $frozenBodyPath -Value $frozenBodyOriginal.Replace($strongAssertion.Trim(), 'act.Should().NotBeNull();')
        } `
        -Restore { Write-Utf8File -Path $frozenBodyPath -Value $frozenBodyOriginal } `
        -ExpectedCode "$ruleId-FROZEN"

    $dotHiddenProject = Join-Path $staticRoot 'src/tests/.Hidden.Tests/.Hidden.Tests.csproj'
    Assert-StaticRejected -Name 'dot-directory-test-project-is-not-hidden' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $dotHiddenProject -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include="xunit" /></ItemGroup></Project>'
        } `
        -Restore { Remove-Item (Split-Path $dotHiddenProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $upperCaseProject = Join-Path $staticRoot 'src/tests/Hidden.Case.Tests/Hidden.Case.Tests.CSPROJ'
    Assert-StaticRejected -Name 'case-variant-test-project-is-scanned' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $upperCaseProject -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
        } `
        -Restore { Remove-Item (Split-Path $upperCaseProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $dotHiddenWorkflow = Join-Path $staticRoot '.github/workflows/.duplicate-cloud-check.yml'
    Assert-StaticRejected -Name 'dot-workflow-cannot-shadow-required-check' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $dotHiddenWorkflow -Value "name: cloud-ci`non: { pull_request: {} }`njobs: { build-test: { runs-on: ubuntu-24.04, steps: [] } }`n" } `
        -Restore { Remove-Item $dotHiddenWorkflow -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $dotHiddenRunSettings = Join-Path $staticRoot '.hidden.runsettings'
    Assert-StaticRejected -Name 'dot-runsettings-cannot-filter-required-tests' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $dotHiddenRunSettings -Value '<RunSettings><RunConfiguration><TestCaseFilter>Category!=Required</TestCaseFilter></RunConfiguration></RunSettings>' } `
        -Restore { Remove-Item $dotHiddenRunSettings -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $nestedBuildProps = Join-Path $staticRoot 'src/Directory.Build.props'
    Assert-StaticRejected -Name 'nested-directory-build-props-cannot-change-restore-graph' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $nestedBuildProps -Value '<Project><Target Name="BeforeRestoreBypass" BeforeTargets="Restore"><Message Text="bypass" /></Target></Project>' } `
        -Restore { Remove-Item $nestedBuildProps -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $directorySolutionPropsPath = Join-Path $staticRoot 'Directory.Solution.props'
    Assert-StaticRejected -Name 'directory-solution-props-cannot-change-reviewed-solution-build' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $directorySolutionPropsPath -Value '<Project><PropertyGroup><RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources></PropertyGroup></Project>' } `
        -Restore { Remove-Item $directorySolutionPropsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $directorySolutionTargetsPath = Join-Path $staticRoot 'Directory.Solution.targets'
    Assert-StaticRejected -Name 'directory-solution-targets-cannot-run-after-static-gate' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $directorySolutionTargetsPath -Value '<Project><Target Name="UnreviewedSolutionBuildHook" BeforeTargets="Build"><Error Text="bypass" /></Target></Project>' } `
        -Restore { Remove-Item $directorySolutionTargetsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $beforeSolutionTargetsPath = Join-Path $staticRoot 'before.IIoT.CloudPlatform.sln.targets'
    Assert-StaticRejected -Name 'before-solution-target-hook-cannot-run-after-static-gate' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $beforeSolutionTargetsPath -Value '<Project><Target Name="UnreviewedBeforeSolutionHook" BeforeTargets="Build"><Error Text="bypass" /></Target></Project>' } `
        -Restore { Remove-Item $beforeSolutionTargetsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $afterSolutionTargetsPath = Join-Path $staticRoot 'after.IIoT.CloudPlatform.sln.targets'
    Assert-StaticRejected -Name 'after-solution-target-hook-cannot-run-after-static-gate' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $afterSolutionTargetsPath -Value '<Project><Target Name="UnreviewedAfterSolutionHook" AfterTargets="Build"><Error Text="bypass" /></Target></Project>' } `
        -Restore { Remove-Item $afterSolutionTargetsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $projectUserHookPath = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj.user'
    Assert-StaticRejected -Name 'project-user-file-cannot-inject-build-target' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $projectUserHookPath -Value '<Project><Target Name="UnreviewedProjectUserHook" BeforeTargets="Build"><Error Text="bypass" /></Target></Project>' } `
        -Restore { Remove-Item $projectUserHookPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $productionObjHookPath = Join-Path $staticRoot 'src/hosts/IIoT.HttpApi/obj/IIoT.HttpApi.csproj.audit.targets'
    & git -C $staticRoot init -q
    & git -C $staticRoot config user.email 'fixture@example.test'
    & git -C $staticRoot config user.name 'Cloud Governance Fixture'
    Assert-StaticRejected -Name 'force-tracked-production-obj-project-hook-cannot-inject-build-target' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $productionObjHookPath -Value '<Project><Target Name="UnreviewedObjProjectHook" BeforeTargets="Build"><Error Text="bypass" /></Target></Project>'
            & git -C $staticRoot add -f -- 'src/hosts/IIoT.HttpApi/obj/IIoT.HttpApi.csproj.audit.targets'
            if ($LASTEXITCODE -ne 0) { throw 'Could not force-track the production obj project-hook fixture.' }
        } `
        -Restore {
            & git -C $staticRoot rm --cached -q --ignore-unmatch -- 'src/hosts/IIoT.HttpApi/obj/IIoT.HttpApi.csproj.audit.targets'
            Remove-Item $productionObjHookPath -Force -ErrorAction SilentlyContinue
        } `
        -ExpectedCode "$ruleId-BYPASS"

    $editorConfigPath = Join-Path $staticRoot '.editorconfig'
    Assert-StaticRejected -Name 'editorconfig-cannot-suppress-reviewed-analyzer-diagnostics' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $editorConfigPath -Value "root = true`n[*.cs]`ndotnet_diagnostic.CA2000.severity = none`n" } `
        -Restore { Remove-Item $editorConfigPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $globalConfigPath = Join-Path $staticRoot '.globalconfig'
    Assert-StaticRejected -Name 'globalconfig-cannot-suppress-reviewed-analyzer-diagnostics' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $globalConfigPath -Value "is_global = true`ndotnet_diagnostic.CA2000.severity = none`n" } `
        -Restore { Remove-Item $globalConfigPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $runtimeConfigTemplatePath = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/runtimeconfig.template.json'
    Assert-StaticRejected -Name 'runtimeconfig-template-cannot-change-required-test-runtime' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $runtimeConfigTemplatePath -Value '{"configProperties":{"System.GC.Concurrent":false}}' } `
        -Restore { Remove-Item $runtimeConfigTemplatePath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $rootBuildPropsPath = Join-Path $staticRoot 'Directory.Build.props'
    $rootBuildPropsOriginal = Get-Content $rootBuildPropsPath -Raw
    Assert-StaticRejected -Name 'nuget-audit-cannot-be-disabled' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $rootBuildPropsPath -Value $rootBuildPropsOriginal.Replace('<NuGetAudit>true</NuGetAudit>', '<NuGetAudit>false</NuGetAudit>') } `
        -Restore { Write-Utf8File -Path $rootBuildPropsPath -Value $rootBuildPropsOriginal } `
        -ExpectedCode "$ruleId-DEPENDENCY"

    Assert-StaticRejected -Name 'nuget-audit-code-cannot-be-demoted-from-error' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $rootBuildPropsPath -Value $rootBuildPropsOriginal.Replace(';NU1905</WarningsAsErrors>', '</WarningsAsErrors>') } `
        -Restore { Write-Utf8File -Path $rootBuildPropsPath -Value $rootBuildPropsOriginal } `
        -ExpectedCode "$ruleId-DEPENDENCY"

    $sharedTestBuildPropsPath = Join-Path $staticRoot 'src/tests/Directory.Build.props'
    $sharedTestBuildPropsOriginal = Get-Content $sharedTestBuildPropsPath -Raw
    Assert-StaticRejected -Name 'test-projects-cannot-shadow-root-nuget-audit' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $sharedTestBuildPropsPath -Value $sharedTestBuildPropsOriginal.Replace('  <Import Project="$(MSBuildThisFileDirectory)../../Directory.Build.props" />' + "`n", '')
        } `
        -Restore { Write-Utf8File -Path $sharedTestBuildPropsPath -Value $sharedTestBuildPropsOriginal } `
        -ExpectedCode "$ruleId-DEPENDENCY"

    $nugetConfigPath = Join-Path $staticRoot 'NuGet.Config'
    Assert-StaticRejected -Name 'new-nuget-config-cannot-change-reviewed-restore' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $nugetConfigPath -Value '<configuration><packageSources><add key="unreviewed" value="https://packages.invalid/v3/index.json" /></packageSources></configuration>' } `
        -Restore { Remove-Item $nugetConfigPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $centralPackagesPath = Join-Path $staticRoot 'Directory.Packages.props'
    Assert-StaticRejected -Name 'new-central-package-file-cannot-change-reviewed-restore' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $centralPackagesPath -Value '<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>' } `
        -Restore { Remove-Item $centralPackagesPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $npmConfigPath = Join-Path $staticRoot 'src/ui/iiot-web/.npmrc'
    Assert-StaticRejected -Name 'new-npmrc-cannot-change-reviewed-restore' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $npmConfigPath -Value 'registry=https://packages.invalid/' } `
        -Restore { Remove-Item $npmConfigPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $npmShrinkwrapPath = Join-Path $staticRoot 'src/ui/iiot-web/npm-shrinkwrap.json'
    Assert-StaticRejected -Name 'npm-shrinkwrap-cannot-shadow-reviewed-package-lock' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $npmShrinkwrapPath -Value '{"name":"fixture","lockfileVersion":3,"packages":{}}' } `
        -Restore { Remove-Item $npmShrinkwrapPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $dotHiddenTargets = Join-Path $staticRoot '.hidden/Directory.Build.targets'
    Assert-StaticRejected -Name 'dot-directory-build-targets-cannot-shadow-gate' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $dotHiddenTargets -Value '<Project />' } `
        -Restore { Remove-Item (Split-Path $dotHiddenTargets -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $responseFilePath = Join-Path $staticRoot 'MSBuild.rsp'
    Assert-StaticRejected -Name 'automatic-msbuild-response-file-cannot-alter-msbuild' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $responseFilePath -Value '-p:ImportDirectoryBuildTargets=false' } `
        -Restore { Remove-Item $responseFilePath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS-RESPONSE"

    $scannerBaselinePath = Join-Path $staticRoot 'scripts/tests/baselines/cloud-test-governance.baseline.json'
    $scannerBaselineOriginal = Get-Content $scannerBaselinePath -Raw
    Assert-StaticRejected -Name 'baseline-scanner-contract-cannot-be-forged' -ValidationRoot $staticRoot `
        -Mutate {
            $tampered = $scannerBaselineOriginal | ConvertFrom-Json -Depth 100
            $tampered.scanner.metadataLoadContextSha256ByPlatform.'Linux-X64' = ('f' * 64)
            Write-JsonFile -Value $tampered -Path $scannerBaselinePath
        } `
        -Restore { Write-Utf8File -Path $scannerBaselinePath -Value $scannerBaselineOriginal } `
        -ExpectedCode "$ruleId-SCAN"

    $gitAttributesPath = Join-Path $staticRoot '.gitattributes'
    $gitAttributesOriginal = Get-Content $gitAttributesPath -Raw
    Assert-StaticRejected -Name 'protected-assets-must-remain-lf-normalized' -ValidationRoot $staticRoot `
        -Mutate {
            [IO.File]::WriteAllText($gitAttributesPath, $gitAttributesOriginal.Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($false))
        } `
        -Restore { Write-Utf8File -Path $gitAttributesPath -Value $gitAttributesOriginal } `
        -ExpectedCode "$ruleId-CONFIG"

    $forceTrackedSkipPath = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/obj/ForceTrackedRuntimeSkip.cs'
    & git -C $staticRoot init -q
    & git -C $staticRoot config user.email 'fixture@example.test'
    & git -C $staticRoot config user.name 'Cloud Governance Fixture'
    Assert-StaticRejected -Name 'force-tracked-ignored-source-cannot-hide-runtime-skip' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $forceTrackedSkipPath -Value 'namespace IIoT.ServiceLayer.Tests; internal static class ForceTrackedRuntimeSkip { public static void Run() => Xunit.Assert.Skip("hidden"); }'
            & git -C $staticRoot add -f -- 'src/tests/IIoT.ServiceLayer.Tests/obj/ForceTrackedRuntimeSkip.cs'
            $cachedPaths = @(& git -C $staticRoot ls-files --cached)
            if ('src/tests/IIoT.ServiceLayer.Tests/obj/ForceTrackedRuntimeSkip.cs' -notin $cachedPaths) {
                throw 'Force-tracked fixture did not enter the temporary Git index.'
            }
        } `
        -Restore {
            & git -C $staticRoot rm --cached -q --ignore-unmatch -- 'src/tests/IIoT.ServiceLayer.Tests/obj/ForceTrackedRuntimeSkip.cs'
            Remove-Item $forceTrackedSkipPath -Force -ErrorAction SilentlyContinue
        } `
        -ExpectedCode "$ruleId-DISABLED"

    $gitLinkFixturePath = 'external-governance-bypass'
    Assert-StaticRejected -Name 'gitlink-mode-cannot-hide-governed-inputs' -ValidationRoot $staticRoot `
        -Mutate {
            $fixtureObject = (& git -C $staticRoot hash-object '.gitattributes' | Out-String).Trim()
            & git -C $staticRoot update-index --add --cacheinfo "160000,$fixtureObject,$gitLinkFixturePath"
            if ($LASTEXITCODE -ne 0) { throw 'Could not create the temporary Git gitlink-mode fixture.' }
        } `
        -Restore { & git -C $staticRoot update-index --force-remove -- $gitLinkFixturePath } `
        -ExpectedCode "$ruleId-BYPASS"

    if (-not $IsWindows) {
        $directorySymlinkPath = Join-Path $staticRoot 'src/tests/LinkedGovernanceBypass'
        Assert-StaticRejected -Name 'directory-symlink-cannot-escape-governed-inventory' -ValidationRoot $staticRoot `
            -Mutate {
                [void](New-Item -ItemType SymbolicLink -Path $directorySymlinkPath -Target (Join-Path $staticRoot 'src/core'))
            } `
            -Restore { Remove-Item -LiteralPath $directorySymlinkPath -Force -ErrorAction SilentlyContinue } `
            -ExpectedCode "$ruleId-BYPASS"
    }

    $duplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check.yml'
    Assert-StaticRejected -Name 'duplicate-required-check-identity' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $duplicateWorkflowPath -Value @'
name: cloud-ci
on:
  workflow_dispatch:
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - run: true
'@
        } `
        -Restore { Remove-Item $duplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $quotedDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-quoted.yml'
    Assert-StaticRejected -Name 'quoted-duplicate-workflow-name' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $quotedDuplicateWorkflowPath -Value @'
name: "cloud-ci"
on: { workflow_dispatch: {} }
jobs:
  other:
    runs-on: ubuntu-latest
    steps: []
'@
        } `
        -Restore { Remove-Item $quotedDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $displayNameDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-display.yml'
    Assert-StaticRejected -Name 'duplicate-job-display-name' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $displayNameDuplicateWorkflowPath -Value @'
name: other-workflow
on: { workflow_dispatch: {} }
jobs:
  other:
    name: build-test
    runs-on: ubuntu-latest
    steps: []
'@
        } `
        -Restore { Remove-Item $displayNameDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $inlineDuplicateWorkflowPath = Join-Path $staticRoot '.github/workflows/duplicate-cloud-check-inline.yml'
    Assert-StaticRejected -Name 'inline-duplicate-job-key' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $inlineDuplicateWorkflowPath -Value @'
name: other-workflow
on: { workflow_dispatch: {} }
jobs: { "build-test" : { runs-on: ubuntu-latest, steps: [] } }
'@
        } `
        -Restore { Remove-Item $inlineDuplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CI"

    $hiddenIsTestProject = Join-Path $staticRoot 'src/tests/Hidden.IsTestProject.Tests/Hidden.IsTestProject.Tests.csproj'
    Assert-StaticRejected -Name 'hidden-is-test-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenIsTestProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenIsTestProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $hiddenPackageProject = Join-Path $staticRoot 'src/tests/Hidden.Package.Tests/Hidden.Package.Tests.csproj'
    Assert-StaticRejected -Name 'hidden-test-package-project' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenPackageProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="xunit.v3" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenPackageProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-PROJECT"

    $hiddenNUnitProject = Join-Path $staticRoot 'src/Hidden.NUnit.Tests/Hidden.NUnit.Tests.csproj'
    Assert-StaticRejected -Name 'non-xunit-test-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenNUnitProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="NUnit" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenNUnitProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenMSTestProject = Join-Path $staticRoot 'src/Hidden.MSTest.Tests/Hidden.MSTest.Tests.csproj'
    Assert-StaticRejected -Name 'mstest-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenMSTestProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="MSTest.TestFramework" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenMSTestProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTUnitProject = Join-Path $staticRoot 'src/Hidden.TUnit.Tests/Hidden.TUnit.Tests.csproj'
    Assert-StaticRejected -Name 'tunit-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTUnitProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="TUnit" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTUnitProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTestingPlatformProject = Join-Path $staticRoot 'src/Hidden.TestingPlatform.Tests/Hidden.TestingPlatform.Tests.csproj'
    Assert-StaticRejected -Name 'microsoft-testing-platform-project-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTestingPlatformProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.Testing.Platform" /></ItemGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTestingPlatformProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $hiddenTestingPlatformMarkerProject = Join-Path $staticRoot 'src/Hidden.TestingPlatformMarker.Tests/Hidden.TestingPlatformMarker.Tests.csproj'
    Assert-StaticRejected -Name 'testing-platform-marker-outside-reviewed-root' -ValidationRoot $staticRoot `
        -Mutate {
            Write-Utf8File -Path $hiddenTestingPlatformMarkerProject -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><TestingPlatformApplication>true</TestingPlatformApplication></PropertyGroup>
</Project>
'@
        } `
        -Restore { Remove-Item (Split-Path $hiddenTestingPlatformMarkerProject -Parent) -Recurse -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-BYPASS"

    $serviceLayerProject = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj'
    $serviceLayerProjectOriginal = Get-Content $serviceLayerProject -Raw
    Assert-StaticRejected -Name 'conditional-is-test-project-cannot-bypass-build-gate' -ValidationRoot $staticRoot `
        -Mutate {
            $mutated = $serviceLayerProjectOriginal.Replace(
                '<IsTestProject>true</IsTestProject>',
                '<IsTestProject Condition="''$(CI)'' == ''true''">true</IsTestProject>')
            Write-Utf8File -Path $serviceLayerProject -Value $mutated
        } `
        -Restore { Write-Utf8File -Path $serviceLayerProject -Value $serviceLayerProjectOriginal } `
        -ExpectedCode "$ruleId-BYPASS"

    Assert-StaticRejected -Name 'project-runner-override' -ValidationRoot $staticRoot `
        -Mutate {
            $mutated = $serviceLayerProjectOriginal.Replace(
                '</Project>',
                "  <ItemGroup><None Include=`"disabled.runner.json`" Link=`"xunit.runner.json`" CopyToOutputDirectory=`"Always`" /></ItemGroup>`n</Project>")
            Write-Utf8File -Path $serviceLayerProject -Value $mutated
        } `
        -Restore { Write-Utf8File -Path $serviceLayerProject -Value $serviceLayerProjectOriginal } `
        -ExpectedCode "$ruleId-CONFIG"

    $assemblyRunnerConfig = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.xunit.runner.json'
    Assert-StaticRejected -Name 'assembly-specific-runner-config' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $assemblyRunnerConfig -Value "{`"failSkips`":false}`n" } `
        -Restore { Remove-Item $assemblyRunnerConfig -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $runSettingsPath = Join-Path $staticRoot 'cloud.runsettings'
    Assert-StaticRejected -Name 'runsettings-cannot-filter-required-tests' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $runSettingsPath -Value '<RunSettings><RunConfiguration><TestCaseFilter>Category!=Required</TestCaseFilter></RunConfiguration></RunSettings>' } `
        -Restore { Remove-Item $runSettingsPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-CONFIG"

    $assertSkipPath = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/RuntimeSkipBypassFixture.cs'
    Assert-StaticRejected -Name 'runtime-assert-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $assertSkipPath -Value 'namespace IIoT.ServiceLayer.Tests; internal static class RuntimeSkipBypassFixture { public static void Bypass() => Xunit.Assert.Skip("disabled"); }' } `
        -Restore { Remove-Item $assertSkipPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-DISABLED"

    $aliasedAssertSkipPath = Join-Path $staticRoot 'src/tests/IIoT.ServiceLayer.Tests/AliasedRuntimeSkipBypassFixture.cs'
    Assert-StaticRejected -Name 'aliased-runtime-skip' -ValidationRoot $staticRoot `
        -Mutate { Write-Utf8File -Path $aliasedAssertSkipPath -Value 'using Q = global::Xunit.Assert; namespace IIoT.ServiceLayer.Tests; internal static class AliasedRuntimeSkipBypassFixture { public static void Bypass() => Q.Skip("disabled"); }' } `
        -Restore { Remove-Item $aliasedAssertSkipPath -Force -ErrorAction SilentlyContinue } `
        -ExpectedCode "$ruleId-DISABLED"

    $workflowPath = Join-Path $staticRoot '.github/workflows/cloud-ci.yml'
    $workflowOriginal = Get-Content $workflowPath -Raw
    Assert-WorkflowSemanticRejected -Name 'required-workflow-actions-must-use-full-commit-sha' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace(
                'actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7',
                'actions/checkout@v7')
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage "uses movable or non-canonical Action reference 'actions/checkout@v7'" `
        -ExpectedCode "$ruleId-CI-ACTION"

    Assert-WorkflowSemanticRejected -Name 'workflow-dispatch-must-be-restricted-to-main' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            $dispatchGuard = @'
          $eventName = '${{ github.event_name }}'
          $eventRef = '${{ github.ref }}'
          if ($eventName -eq 'workflow_dispatch' -and $eventRef -ne 'refs/heads/main') {
            throw 'workflow_dispatch is trusted only on refs/heads/main.'
          }
'@
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace($dispatchGuard, '')
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage "step 'Validate immutable test baseline anchor' must preserve its exact run body/shell" `
        -ExpectedCode "$ruleId-CI-STEP"

    $reviewedPreRestore = @'
      - name: Validate reviewed restore and build inputs
        shell: pwsh
        run: ./scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release

      - name: Run Cloud test governance self-tests
        shell: pwsh
        run: ./scripts/tests/TestCloudTestGovernanceBehavior.ps1
'@
    $reviewedRestore = @'
      - name: Restore cloud solution
        run: dotnet restore IIoT.CloudPlatform.slnx --disable-build-servers --nologo -noAutoResponse
'@
    Assert-WorkflowSemanticRejected -Name 'restore-cannot-run-before-reviewed-static-input-gate' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            if (-not $workflowOriginal.Contains($reviewedPreRestore.Trim(), [StringComparison]::Ordinal) -or
                -not $workflowOriginal.Contains($reviewedRestore.Trim(), [StringComparison]::Ordinal)) {
                throw 'Restore-order fixture could not locate reviewed workflow steps.'
            }
            $withoutRestore = $workflowOriginal.Replace($reviewedRestore.Trim(), '__RESTORE_STEP__')
            $mutated = $withoutRestore.Replace($reviewedPreRestore.Trim(), "$($reviewedRestore.Trim())`n`n$($reviewedPreRestore.Trim())").Replace('__RESTORE_STEP__', '')
            Write-Utf8File -Path $workflowPath -Value $mutated
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'outside the canonical trust/execution order' `
        -ExpectedCode "$ruleId-CI-STEP"

    Assert-WorkflowSemanticRejected -Name 'top-level-workflow-permissions-cannot-escalate' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace("permissions:`n  contents: read", 'permissions: write-all')
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'weakens trigger, permissions, shell fail-fast, or command failure semantics' `
        -ExpectedCode "$ruleId-CI-SECURITY"

    Assert-WorkflowSemanticRejected -Name 'pull-request-target-cannot-enter-required-workflow' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate { Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace("on:`n", "on:`n  pull_request_target: {}`n") } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'weakens trigger, permissions, shell fail-fast, or command failure semantics' `
        -ExpectedCode "$ruleId-CI-SECURITY"

    Assert-WorkflowSemanticRejected -Name 'main-push-path-filter-cannot-hide-new-root-inputs' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace(
                "  push:`n    branches: [main]`n",
                "  push:`n    branches: [main]`n    paths:`n      - `"src/**`"`n")
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'main push trigger must not use path filters' `
        -ExpectedCode "$ruleId-CI-TRIGGER"

    Assert-WorkflowSemanticRejected -Name 'required-dotnet-command-cannot-enable-auto-response' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            $restoreCommand = 'dotnet restore IIoT.CloudPlatform.slnx --disable-build-servers --nologo -noAutoResponse'
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace($restoreCommand, $restoreCommand.Replace(' -noAutoResponse', ''))
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'dotnet command does not disable automatic response files' `
        -ExpectedCode "$ruleId-BYPASS-RESPONSE"

    Assert-WorkflowSemanticRejected -Name 'required-command-cannot-hide-in-dead-shell-branch' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            $requiredLine = '        run: ./scripts/tests/TestCloudTestGovernanceBehavior.ps1'
            if (-not $workflowOriginal.Contains($requiredLine, [StringComparison]::Ordinal)) {
                throw 'Dead-branch fixture could not locate the reviewed governance command.'
            }
            $deadBranch = @'
        run: |
          if false; then
            ./scripts/tests/TestCloudTestGovernanceBehavior.ps1
          fi
'@
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace($requiredLine, $deadBranch.TrimEnd())
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage "step 'Run Cloud test governance self-tests' must preserve its exact run body/shell" `
        -ExpectedCode "$ruleId-CI-STEP"

    Assert-WorkflowSemanticRejected -Name 'manual-end-to-end-must-wait-for-trusted-gate' -ValidationRoot $staticRoot -WorkflowPath $workflowPath `
        -Mutate {
            Write-Utf8File -Path $workflowPath -Value $workflowOriginal.Replace("  full-end-to-end:`n    needs: build-test`n", "  full-end-to-end:`n")
        } `
        -Restore { Write-Utf8File -Path $workflowPath -Value $workflowOriginal } `
        -ExpectedMessage 'manual full-end-to-end job must wait for the trusted build-test gate' `
        -ExpectedCode "$ruleId-CI-ORDER"

    $runnerFixtureRoot = Join-Path $tempRoot 'runner-output'
    $tamperedRunnerConfig = Join-Path $runnerFixtureRoot 'xunit.runner.json'
    Write-Utf8File -Path $tamperedRunnerConfig -Value "{`n  `"failSkips`": false`n}`n"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $runnerOutput = & pwsh -NoLogo -NoProfile -File $policyPath `
            -Mode ValidateRunnerConfiguration `
            -RepositoryRoot $RepositoryRoot `
            -RunnerConfigPath $tamperedRunnerConfig 2>&1
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $runnerText = ($runnerOutput | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -or -not $runnerText.Contains("$ruleId-DISABLED", [StringComparison]::Ordinal)) {
        throw "Tampered output runner configuration should fail with $ruleId-DISABLED:`n$runnerText"
    }
    Write-Host "Rejected Cloud output-governance fixture: failSkips=false ($ruleId-DISABLED)"
    $script:rejectedFixtureCount++

    & git -C $staticRoot add -A
    & git -C $staticRoot commit -q -m 'fixture trusted baseline'
    $trustedBaselineCommit = (& git -C $staticRoot rev-parse HEAD | Out-String).Trim()
    $selfAnchorOutput = & pwsh -NoLogo -NoProfile -File (Join-Path $staticRoot 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Mode ValidateBaselineAnchor `
        -RepositoryRoot $staticRoot `
        -TrustedBaseRevision $trustedBaselineCommit 2>&1
    $selfAnchorText = ($selfAnchorOutput | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -or
        -not $selfAnchorText.Contains("$ruleId-BASELINE-SELF", [StringComparison]::Ordinal)) {
        throw "Candidate HEAD must never self-anchor its baseline:`n$selfAnchorText"
    }
    Write-Host "Rejected Cloud baseline-anchor fixture: candidate-head-cannot-be-trusted-base ($ruleId-BASELINE)"
    $script:rejectedFixtureCount++

    $anchorBaseline = Get-Content $scannerBaselinePath -Raw | ConvertFrom-Json -Depth 100
    $anchorBaseline.generatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(1).ToString('O')
    Write-JsonFile -Value $anchorBaseline -Path $scannerBaselinePath
    & git -C $staticRoot add -- 'scripts/tests/baselines/cloud-test-governance.baseline.json'
    & git -C $staticRoot commit -q -m 'fixture self-approved baseline rewrite'
    $anchorOutput = & pwsh -NoLogo -NoProfile -File (Join-Path $staticRoot 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Mode ValidateBaselineAnchor `
        -RepositoryRoot $staticRoot `
        -TrustedBaseRevision $trustedBaselineCommit 2>&1
    $anchorText = ($anchorOutput | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -or -not $anchorText.Contains("$ruleId-BASELINE", [StringComparison]::Ordinal)) {
        throw "Head baseline rewrite should be rejected against the trusted base:`n$anchorText"
    }
    Write-Host "Rejected Cloud baseline-anchor fixture: head-cannot-rewrite-trusted-baseline ($ruleId-BASELINE)"
    $script:rejectedFixtureCount++

    $genesisRoot = Join-Path $tempRoot 'genesis-repository'
    & git clone --quiet --no-hardlinks $RepositoryRoot $genesisRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone the local repository for the real genesis anchor fixture.' }
    & git -C $genesisRoot checkout -q 'ef9d9dbef87e0e561c98815c83b4775670991c0b'
    & git -C $genesisRoot config user.email 'fixture@example.test'
    & git -C $genesisRoot config user.name 'Cloud Governance Fixture'
    & git -C $genesisRoot config core.quotePath true
    $genesisPaths = @(
        '.gitattributes', '.github/CODEOWNERS', '.github/workflows/cloud-ci.yml', 'Directory.Build.props', 'Directory.Build.targets', 'global.json',
        'scripts/tests', 'deploy/tests/deployment-behavior.sh', 'src/tests/Directory.Build.props', 'src/tests/xunit.runner.json',
        'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj',
        'docs/云端架构治理清单.md', 'docs/云端规则.md', 'docs/改动复盘与规则沉淀.md'
    )
    foreach ($relativePath in $genesisPaths) {
        $source = Join-Path $RepositoryRoot $relativePath
        $destination = Join-Path $genesisRoot $relativePath
        Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue
        [void](New-Item (Split-Path $destination -Parent) -ItemType Directory -Force)
        Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
    }
    & git -C $genesisRoot add -A
    & git -C $genesisRoot commit -q -m 'fixture one-time governance genesis'
    $genesisOutput = & pwsh -NoLogo -NoProfile -File (Join-Path $genesisRoot 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Mode ValidateBaselineAnchor `
        -RepositoryRoot $genesisRoot `
        -TrustedBaseRevision 'ef9d9dbef87e0e561c98815c83b4775670991c0b' 2>&1
    $genesisText = ($genesisOutput | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not $genesisText.Contains('Cloud one-time baseline genesis anchor passed', [StringComparison]::Ordinal)) {
        throw "The real ef9d9db-to-candidate genesis anchor should pass:`n$genesisText"
    }
    Write-Host 'Accepted Cloud baseline-anchor fixture: real-ef9d9db-genesis-candidate'
    $script:acceptedFixtureCount++

    $forbiddenGenesisPath = Join-Path $genesisRoot 'src/core/IIoT.Core.Identity/IIoT.Core.Identity.csproj'
    [IO.File]::AppendAllText($forbiddenGenesisPath, "`n<!-- forbidden genesis fixture -->`n", [Text.UTF8Encoding]::new($false))
    & git -C $genesisRoot add -- 'src/core/IIoT.Core.Identity/IIoT.Core.Identity.csproj'
    & git -C $genesisRoot commit -q --amend --no-edit
    $forbiddenGenesisOutput = & pwsh -NoLogo -NoProfile -File (Join-Path $genesisRoot 'scripts/tests/TestCloudTestGovernancePolicy.ps1') `
        -Mode ValidateBaselineAnchor `
        -RepositoryRoot $genesisRoot `
        -TrustedBaseRevision 'ef9d9dbef87e0e561c98815c83b4775670991c0b' 2>&1
    $forbiddenGenesisText = ($forbiddenGenesisOutput | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -or
        -not $forbiddenGenesisText.Contains("$ruleId-BASELINE-GENESIS-PATH", [StringComparison]::Ordinal)) {
        throw "A production-file mutation must be rejected from the one-time genesis transition:`n$forbiddenGenesisText"
    }
    Write-Host "Rejected Cloud baseline-anchor fixture: genesis-cannot-change-production-input ($ruleId-BASELINE)"
    $script:rejectedFixtureCount++

    Write-Host "Cloud test-governance behavior fixtures passed: accepted=$script:acceptedFixtureCount rejected=$script:rejectedFixtureCount."
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# GitHub's pwsh runner appends `exit $LASTEXITCODE` after dot-sourcing the step
# script. Expected negative fixtures intentionally leave a non-zero native exit
# code, so a successful self-test must reset the process outcome explicitly.
exit 0
