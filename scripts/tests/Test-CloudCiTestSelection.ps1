[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $RepositoryRoot).Path
$selector = Join-Path $root 'scripts/tests/Select-CloudCiTests.ps1'
$allowedCategories = @('Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')
function Assert-ValidCategories([object]$Selection) {
    $invalid = @($Selection.selectedDotNetProjects.categories | Where-Object {
            $_ -notin $allowedCategories
        })
    if ($invalid.Count -gt 0) {
        throw "Selector emitted non-canonical categories: $($invalid -join ', ')"
    }
}

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $fullPath = Join-Path $Root $Path
    [void](New-Item (Split-Path $fullPath -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $fullPath,
        $Content.Trim() + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Set-FixtureSolution {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$BusinessProjectPaths
    )

    $businessProjects = @($BusinessProjectPaths | Sort-Object | ForEach-Object {
            "  <Project Path=`"$_`" />"
        }) -join [Environment]::NewLine
    Write-FixtureFile -Root $Root -Path 'IIoT.CloudPlatform.slnx' -Content @"
<Solution>
  <Project Path="src/core/Cloud.Product/Cloud.Product.csproj" />
  <Project Path="src/core/Cloud.Other/Cloud.Other.csproj" />
  <Project Path="src/tests/Cloud.Architecture/Cloud.Architecture.csproj" />
$businessProjects
  <Project Path="src/tests/Cloud.Business.Other/Cloud.Business.Other.csproj" />
</Solution>
"@
}

function New-DynamicRunnerFixture {
    param(
        [Parameter(Mandatory)][string]$Root,
        [switch]$IncludeRemainingBusiness,
        [switch]$UnownedLegacy
    )

    Write-FixtureFile -Root $Root -Path 'src/core/Cloud.Product/Cloud.Product.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/core/Cloud.Other/Cloud.Other.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Architecture/Cloud.Architecture.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <CloudTestKind>Architecture</CloudTestKind>
    <CloudTestRuntime>Pure</CloudTestRuntime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/Cloud.Product/Cloud.Product.csproj" />
  </ItemGroup>
</Project>
'@
    Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <CloudTestKind>Unit</CloudTestKind>
    <CloudTestRuntime>Pure</CloudTestRuntime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/Cloud.Product/Cloud.Product.csproj" />
  </ItemGroup>
</Project>
'@
    if ($UnownedLegacy) {
        Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <CloudTestKind>Unit</CloudTestKind>
    <CloudTestRuntime>Pure</CloudTestRuntime>
  </PropertyGroup>
</Project>
'@
    }
    Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Business.Legacy/LegacyTests.cs' -Content 'internal sealed class LegacyTests { }'
    Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Business.Other/Cloud.Business.Other.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <CloudTestKind>Unit</CloudTestKind>
    <CloudTestRuntime>Pure</CloudTestRuntime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/Cloud.Other/Cloud.Other.csproj" />
  </ItemGroup>
</Project>
'@
    $businessProjectPaths = [Collections.Generic.List[string]]::new()
    $businessProjectPaths.Add('src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj')
    if ($IncludeRemainingBusiness) {
        Write-FixtureFile -Root $Root -Path 'src/tests/Cloud.Business.Remaining/Cloud.Business.Remaining.csproj' -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <CloudTestKind>Unit</CloudTestKind>
    <CloudTestRuntime>Pure</CloudTestRuntime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../core/Cloud.Product/Cloud.Product.csproj" />
  </ItemGroup>
</Project>
'@
        $businessProjectPaths.Add(
            'src/tests/Cloud.Business.Remaining/Cloud.Business.Remaining.csproj')
    }
    Set-FixtureSolution `
        -Root $Root `
        -BusinessProjectPaths @($businessProjectPaths)

    & git -C $Root init -q
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize Cloud selector fixture repository.' }
    & git -C $Root add .
    if ($LASTEXITCODE -ne 0) { throw 'Failed to stage Cloud selector fixture repository.' }
    & git -C $Root -c user.name=selector-fixture -c user.email=selector@example.invalid `
        commit -q -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Failed to commit Cloud selector fixture baseline.' }
    return ((& git -C $Root rev-parse HEAD) -join '').Trim()
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cloud-ci-selector-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $temporaryRoot -ItemType Directory -Force)
try {
    $positiveOutput = Join-Path $temporaryRoot 'positive.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('src/core/IIoT.Core.Production/Aggregates/Devices/Device.cs') `
        -OutputPath $positiveOutput `
        -GitHubOutputPath ''
    $positive = Get-Content $positiveOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $positive
    $positiveNames = @($positive.selectedDotNetProjects.projectName)
    if ($positiveNames -notcontains 'IIoT.CloudPlatform.ArchitectureTests' -or
        $positiveNames -notcontains 'IIoT.CloudPlatform.AnalyzerTests' -or
        $positiveNames -notcontains 'IIoT.CloudPlatform.AggregateTests') {
        throw "Positive selector fixture omitted mandatory or affected projects: $($positiveNames -join ', ')"
    }
    if ($positiveNames -contains 'IIoT.CloudPlatform.EndToEndTests' -or
        @($positive.selectedDotNetProjects.categories) -contains 'Quality') {
        throw 'Default source selection included the explicit Quality lane.'
    }

    $docsOutput = Join-Path $temporaryRoot 'docs.json'
    & $selector `
        -RepositoryRoot $root `
        -ChangedFiles @('docs/example.md') `
        -OutputPath $docsOutput `
        -GitHubOutputPath ''
    $docs = Get-Content $docsOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $docs
    if (@($docs.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Security')
            }).Count -ne 0) {
        throw 'Documentation-only changes selected a non-red-line category.'
    }
    $analyzer = @($docs.selectedDotNetProjects | Where-Object projectName -eq 'IIoT.CloudPlatform.AnalyzerTests')
    if ($analyzer.Count -ne 1 -or
        @($analyzer[0].categories) -notcontains 'Architecture' -or
        @($analyzer[0].categories) -notcontains 'Security') {
        throw 'Cloud AnalyzerTests must explicitly cover Architecture and Security without inventory lookup.'
    }

    $manualOutput = Join-Path $temporaryRoot 'manual.json'
    & $selector `
        -RepositoryRoot $root `
        -Mode Quality `
        -OutputPath $manualOutput `
        -GitHubOutputPath ''
    $manual = Get-Content $manualOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $manual
    if ([string]$manual.mode -cne 'Quality' -or
        @($manual.unclassifiedFiles).Count -ne 0 -or
        @($manual.selectedDotNetProjects.categories) -notcontains 'Quality' -or
        @($manual.selectedDotNetProjects.categories) -contains 'Business' -or
        @($manual.selectedDotNetProjects.categories) -contains 'DeploymentContract') {
        throw 'Explicit Quality selection did not stay within red-line plus Quality categories.'
    }

    $deploymentOutput = Join-Path $temporaryRoot 'deployment.json'
    & $selector `
        -RepositoryRoot $root `
        -Mode Deployment `
        -ChangedFiles @(
            'src/core/IIoT.Core.Production/Aggregates/Devices/Device.cs',
            'deploy/Deploy.ps1') `
        -OutputPath $deploymentOutput `
        -GitHubOutputPath ''
    $deployment = Get-Content $deploymentOutput -Raw | ConvertFrom-Json
    if ([string]$deployment.mode -cne 'Deployment' -or
        -not [bool]$deployment.deploymentAffected -or
        @($deployment.selectedDotNetProjects.categories) -notcontains 'DeploymentContract' -or
        @($deployment.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Security', 'DeploymentContract')
            }).Count -ne 0) {
        throw 'Deployment changes did not select only the affected DeploymentContract lane.'
    }

    $deferredOutput = Join-Path $temporaryRoot 'deferred.json'
    & $selector -RepositoryRoot $root -ChangedFiles @(
        'src/tests/IIoT.CloudPlatform.EndToEndTests/IIoT.CloudPlatform.EndToEndTests.csproj') `
        -OutputPath $deferredOutput -GitHubOutputPath ''
    $deferred = Get-Content $deferredOutput -Raw | ConvertFrom-Json
    if (@($deferred.deferredExplicitFiles) -notcontains
        'Quality:src/tests/IIoT.CloudPlatform.EndToEndTests/IIoT.CloudPlatform.EndToEndTests.csproj' -or
        @($deferred.selectedDotNetProjects.categories) -contains 'Quality') {
        throw 'Known Quality changes were not deferred from automatic CI.'
    }

    $dynamicRoot = Join-Path $temporaryRoot 'dynamic-runner'
    $dynamicBase = New-DynamicRunnerFixture -Root $dynamicRoot
    $legacyRoot = Join-Path $dynamicRoot 'src/tests/Cloud.Business.Legacy'
    $currentRoot = Join-Path $dynamicRoot 'src/tests/Cloud.Business.Current'
    Move-Item -LiteralPath $legacyRoot -Destination $currentRoot
    Move-Item `
        -LiteralPath (Join-Path $currentRoot 'Cloud.Business.Legacy.csproj') `
        -Destination (Join-Path $currentRoot 'Cloud.Business.Current.csproj')
    Move-Item `
        -LiteralPath (Join-Path $currentRoot 'LegacyTests.cs') `
        -Destination (Join-Path $currentRoot 'CurrentTests.cs')
    Set-FixtureSolution `
        -Root $dynamicRoot `
        -BusinessProjectPaths @(
            'src/tests/Cloud.Business.Current/Cloud.Business.Current.csproj')

    $dynamicChangedFiles = @(
        'IIoT.CloudPlatform.slnx',
        'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj',
        'src/tests/Cloud.Business.Legacy/LegacyTests.cs',
        'src/tests/Cloud.Business.Current/Cloud.Business.Current.csproj',
        'src/tests/Cloud.Business.Current/CurrentTests.cs')
    $dynamicOutput = Join-Path $temporaryRoot 'dynamic.json'
    & $selector `
        -RepositoryRoot $dynamicRoot `
        -BaseRef $dynamicBase `
        -ChangedFiles $dynamicChangedFiles `
        -OutputPath $dynamicOutput `
        -GitHubOutputPath ''
    $dynamic = Get-Content $dynamicOutput -Raw | ConvertFrom-Json
    Assert-ValidCategories $dynamic
    $dynamicNames = @($dynamic.selectedDotNetProjects.projectName)
    if ($dynamicNames -notcontains 'Cloud.Business.Current' -or
        $dynamicNames -contains 'Cloud.Business.Legacy' -or
        $dynamicNames -contains 'Cloud.Business.Other' -or
        @($dynamic.selectedDotNetProjects.categories | Where-Object {
                $_ -notin @('Architecture', 'Business')
            }).Count -ne 0 -or
        @($dynamic.unclassifiedFiles).Count -ne 0 -or
        @($dynamic.requiredExplicitModes) -contains 'Full' -or
        @($dynamic.retiredBusinessProjects) -notcontains
            'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj') {
        throw 'Business runner add/delete/migration did not stay dynamically scoped to affected Business.'
    }

    $dynamicDeploymentOutput = Join-Path $temporaryRoot 'dynamic-deployment.json'
    & $selector `
        -RepositoryRoot $dynamicRoot `
        -Mode Deployment `
        -BaseRef $dynamicBase `
        -ChangedFiles $dynamicChangedFiles `
        -OutputPath $dynamicDeploymentOutput `
        -GitHubOutputPath ''
    $dynamicDeployment = Get-Content $dynamicDeploymentOutput -Raw | ConvertFrom-Json
    if (@($dynamicDeployment.unclassifiedFiles).Count -ne 0 -or
        @($dynamicDeployment.requiredExplicitModes) -contains 'Full' -or
        @($dynamicDeployment.selectedDotNetProjects |
            ForEach-Object { @($_.categories) }) -contains 'Business' -or
        @($dynamicDeployment.retiredBusinessProjects) -notcontains
            'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj') {
        throw 'Deployment mode did not defer a baseline-attributed Business runner migration.'
    }

    $retirementRoot = Join-Path $temporaryRoot 'retired-runner'
    $retirementBase = New-DynamicRunnerFixture `
        -Root $retirementRoot `
        -IncludeRemainingBusiness
    Remove-Item (Join-Path $retirementRoot 'src/tests/Cloud.Business.Legacy') `
        -Recurse `
        -Force
    Set-FixtureSolution `
        -Root $retirementRoot `
        -BusinessProjectPaths @(
            'src/tests/Cloud.Business.Remaining/Cloud.Business.Remaining.csproj')
    $retirementOutput = Join-Path $temporaryRoot 'retired-runner.json'
    & $selector `
        -RepositoryRoot $retirementRoot `
        -BaseRef $retirementBase `
        -ChangedFiles @(
            'IIoT.CloudPlatform.slnx',
            'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj',
            'src/tests/Cloud.Business.Legacy/LegacyTests.cs') `
        -OutputPath $retirementOutput `
        -GitHubOutputPath ''
    $retirement = Get-Content $retirementOutput -Raw | ConvertFrom-Json
    $retirementBusinessNames = @($retirement.selectedDotNetProjects |
        Where-Object { @($_.categories) -contains 'Business' } |
        Select-Object -ExpandProperty projectName)
    if ($retirementBusinessNames.Count -ne 1 -or
        $retirementBusinessNames -notcontains 'Cloud.Business.Remaining' -or
        $retirementBusinessNames -contains 'Cloud.Business.Other' -or
        @($retirement.unclassifiedFiles).Count -ne 0 -or
        @($retirement.requiredExplicitModes) -contains 'Full') {
        throw "Deleted Business runner did not select only its surviving source-owner scope: $($retirementBusinessNames -join ', ')"
    }

    $unownedRetirementRoot = Join-Path $temporaryRoot 'unowned-retired-runner'
    $unownedRetirementBase = New-DynamicRunnerFixture `
        -Root $unownedRetirementRoot `
        -UnownedLegacy
    Remove-Item (Join-Path $unownedRetirementRoot 'src/tests/Cloud.Business.Legacy') `
        -Recurse `
        -Force
    Set-FixtureSolution -Root $unownedRetirementRoot -BusinessProjectPaths @()
    $unownedRetirementOutput = Join-Path $temporaryRoot 'unowned-retired-runner.json'
    $unownedRetirementFailed = $false
    try {
        & $selector `
            -RepositoryRoot $unownedRetirementRoot `
            -BaseRef $unownedRetirementBase `
            -ChangedFiles @(
                'IIoT.CloudPlatform.slnx',
                'src/tests/Cloud.Business.Legacy/Cloud.Business.Legacy.csproj',
                'src/tests/Cloud.Business.Legacy/LegacyTests.cs') `
            -OutputPath $unownedRetirementOutput `
            -GitHubOutputPath ''
    } catch {
        $unownedRetirementFailed = $_.Exception.Message -match 'cannot safely attribute'
    }
    if (-not $unownedRetirementFailed) {
        throw 'Deleted Business runner without baseline owner evidence did not fail closed.'
    }
    $unownedRetirement = Get-Content $unownedRetirementOutput -Raw | ConvertFrom-Json
    if (@($unownedRetirement.unclassifiedFiles) -notcontains 'IIoT.CloudPlatform.slnx' -or
        @($unownedRetirement.requiredExplicitModes) -notcontains 'Full') {
        throw 'Unowned deleted Business runner did not preserve fail-closed evidence.'
    }

    $crossOutput = Join-Path $temporaryRoot 'cross.json'
    & $selector -RepositoryRoot $root -Mode CrossProject -ChangedFiles @() `
        -OutputPath $crossOutput -GitHubOutputPath ''
    $cross = Get-Content $crossOutput -Raw | ConvertFrom-Json
    if (@($cross.selectedDotNetProjects).Count -ne 1 -or
        @($cross.selectedDotNetProjects.categories | Where-Object { $_ -cne 'CrossProject' }).Count -ne 0) {
        throw 'CrossProject mode emitted a non-cross-project runner.'
    }

    if ((Get-Content $selector -Raw).Contains('cloud-test-inventory.json', [StringComparison]::Ordinal)) {
        throw 'Cloud selector still reads the retired historical test inventory.'
    }

    $negativeOutput = Join-Path $temporaryRoot 'negative.json'
    $negativeFailed = $false
    try {
        & $selector `
            -RepositoryRoot $root `
            -ChangedFiles @('src/Unowned.Business/Unknown.cs') `
            -OutputPath $negativeOutput `
            -GitHubOutputPath ''
    } catch {
        $negativeFailed = $_.Exception.Message -match 'cannot safely attribute' -and
            $_.Exception.Message -match 'src/Unowned\.Business/Unknown\.cs'
    }
    if (-not $negativeFailed) {
        throw 'Unknown business path did not fail closed with the file listed.'
    }
    $negative = Get-Content $negativeOutput -Raw | ConvertFrom-Json
    if (@($negative.unclassifiedFiles) -notcontains 'src/Unowned.Business/Unknown.cs') {
        throw 'Unknown business path is absent from selector evidence.'
    }
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item $temporaryRoot -Recurse -Force
    }
}

$workflowText = Get-Content (Join-Path $root '.github/workflows/cloud-ci.yml') -Raw
if ($workflowText -notmatch '\$selectorInputs\.Count\s+-gt\s+0[\s\S]*?Test-CloudCiTestSelection\.ps1') {
    throw 'Cloud default CI does not gate selector behavior tests on affected selector inputs.'
}
if ($workflowText -match "\`$env:CI_MODE\s+-ne\s+'default'" -or
    ($workflowText.Split('Test-CloudCiTestSelection.ps1', [StringSplitOptions]::None).Length - 1) -ne 2) {
    throw 'Cloud selector behavior tests are still wired to an unrelated explicit mode or cross-project job.'
}
if ($workflowText -notmatch 'if\s*\(\[string\]::IsNullOrWhiteSpace\(\$baseRef\)\)\s*\{\s*\$baseRef\s*=\s*''HEAD\^''') {
    throw 'Cloud manual CI modes do not have a deterministic base ref.'
}

Write-Host 'CLOUD_CI_SELECTION_BEHAVIOR_OK positive=1 docs=1 quality=1 deployment=1 deferred=1 dynamic=1 dynamicDeployment=1 retiredBusiness=1 unownedRetired=1 cross=1 negative=1 workflowGate=1'
