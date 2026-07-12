[CmdletBinding()]
param(
    [ValidateSet('ValidateProject', 'ValidateRepository', 'ValidateSnapshot', 'ValidateRepositorySnapshot', 'ValidateStatic', 'ValidateDiscovery', 'ValidateRunnerConfiguration', 'ValidateAttributePayloadFixture', 'ValidateRunnerIdentityFixture', 'ValidateWorkflowFixture', 'ValidateBaselineAnchor', 'GenerateBaseline')]
    [string]$Mode = 'ValidateProject',
    [string]$RepositoryRoot,
    [string]$ProjectPath,
    [string]$ProjectName,
    [string]$AssemblyPath,
    [string]$ReferencePathsFile,
    [string]$RunnerConfigPath,
    [string]$CurrentSnapshotPath,
    [string]$AttributeFixtureAssemblyPath,
    [string]$WorkflowFixturePath,
    [string]$TrustedBaseRevision,
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [string]$BaselinePath,
    [string]$WaiverPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$AllowBaselineWrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ruleId = 'CLOUD-TEST-GOV-001'
$baselineSchemaVersion = '1.2'
$waiverSchemaVersion = '1.0'
$maximumWaiverDays = 30
$approvedWaiverApprovers = @('ShuJinHao')
$allowedTestKinds = @('Architecture', 'Unit', 'Aggregate', 'Application', 'Contract', 'Conformance', 'Persistence', 'Workflow', 'Integration', 'EndToEnd', 'UI', 'GoldenEval', 'Deployment', 'Performance', 'SoakChaos', 'Security')
$allowedRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'Avalonia', 'Browser', 'Windows', 'LiveExternal')
$allowedRisks = @('P0', 'P1', 'P2')
$allowedOwners = @('Cloud.Architecture', 'Cloud.Domain', 'Cloud.Identity', 'Cloud.Employee', 'Cloud.MasterData', 'Cloud.Production', 'Cloud.Persistence', 'Cloud.Infrastructure', 'Cloud.Http', 'Cloud.AiRead', 'Cloud.Web', 'Cloud.Deployment', 'Cloud.Security', 'Cloud.Tests')
$allowedCapabilities = @('Architecture', 'Authentication', 'Authorization', 'Identity', 'Employee', 'Device', 'Recipe', 'Process', 'Production', 'Capacity', 'DeviceLog', 'PassStation', 'ClientRelease', 'EdgeHost', 'AiRead', 'Oidc', 'Persistence', 'Cache', 'Messaging', 'Deployment', 'Web', 'Configuration', 'TestGovernance')
$allowedTestAttributeTypes = @('Xunit.FactAttribute', 'Xunit.TheoryAttribute', 'IIoT.EndToEndTests.WorkspaceAlignmentFactAttribute')
$approvedDotnetSdk = '10.0.301'
$reviewedBaselineSourceHead = 'ef9d9dbef87e0e561c98815c83b4775670991c0b'
$baselineRepositoryPath = 'scripts/tests/baselines/cloud-test-governance.baseline.json'
$attributeCodecPath = Join-Path $PSScriptRoot 'CloudTestAttributeCodec.psm1'
$approvedMetadataLoadContextSha256ByPlatform = @{
    'Linux-X64' = '6720834363d163a51abdd9b311ed5235ca8d6ee583da8cb23e5ae97f5f219831'
    'Linux-Arm64' = '4f369c05fd9220fef48365c8bdad72019fed5ee027e06924822c6e66f6e3c8eb'
    'macOS-X64' = '09ba6bbbd554c538a26438ee96b1a803b61f94e9219f38ef339dd3268617c2ab'
    'macOS-Arm64' = 'd5aeb7ae95e463315d722fb7f22679658b62959c2d6dc0f5f1be9e45f0cb9c39'
    'Windows-X64' = '8e6af791299bb85c94a3fc3eef2b55621a30ca77025d062944f3578e10057ddb'
}
$xunitRunnerConfigSha256 = '3aaf68ea8927dce2c9ee5404088745084d709c1ff2d00bf41c90d9406d31b8a1'
$allowedTestProjectTargetHashes = @{}
$script:repositoryFilesCacheRoot = ''
$script:repositoryFilesCache = $null

if (-not (Test-Path $attributeCodecPath -PathType Leaf)) {
    throw "$ruleId-SCAN attribute codec is missing: $attributeCodecPath"
}
Import-Module $attributeCodecPath -Force

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$Path
    )
    return [System.IO.Path]::GetRelativePath($BasePath, $Path).Replace('\', '/')
}

function Test-IsNoisyRepositoryDirectory {
    param([Parameter(Mandatory)][string]$Name)

    return $Name -in @('.git', '.vs', '.idea', 'bin', 'obj', 'node_modules', 'TestResults', 'artifacts')
}

function Get-RepositoryFiles {
    param([Parameter(Mandatory)][string]$Root)

    $rootPath = Get-NormalizedPath $Root
    if ([string]$script:repositoryFilesCacheRoot -eq $rootPath -and $null -ne $script:repositoryFilesCache) {
        return [object[]]$script:repositoryFilesCache
    }
    $paths = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)

    function Add-RepositoryFilePath {
        param([Parameter(Mandatory)][string]$CandidatePath)

        $fullPath = Get-NormalizedPath $CandidatePath
        if (-not $fullPath.StartsWith("$rootPath/", [StringComparison]::Ordinal) -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            return
        }
        $relativePath = Get-RelativePath -BasePath $rootPath -Path $fullPath
        $paths[$relativePath] = $fullPath
    }

    function Add-RepositoryDirectory {
        param([Parameter(Mandatory)][string]$DirectoryPath)

        foreach ($entry in @(Get-ChildItem -LiteralPath $DirectoryPath -Force)) {
            if ($entry.PSIsContainer) {
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "$ruleId-BYPASS governed repository directories must not be symlinks/reparse points: '$(Get-RelativePath -BasePath $rootPath -Path $entry.FullName)'."
                }
                if (-not (Test-IsNoisyRepositoryDirectory -Name $entry.Name)) {
                    Add-RepositoryDirectory -DirectoryPath $entry.FullName
                }
                continue
            }
            Add-RepositoryFilePath -CandidatePath $entry.FullName
        }
    }

    Add-RepositoryDirectory -DirectoryPath $rootPath

    $gitPrefix = (& git -C $rootPath rev-parse --show-prefix 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and [string]::IsNullOrWhiteSpace($gitPrefix)) {
        foreach ($relativePath in @(& git -C $rootPath -c core.quotePath=false ls-files --cached --others --exclude-standard)) {
            if ([string]::IsNullOrWhiteSpace([string]$relativePath)) { continue }
            Add-RepositoryFilePath -CandidatePath (Join-Path $rootPath ([string]$relativePath))
        }
        foreach ($stageLine in @(& git -C $rootPath -c core.quotePath=false ls-files --stage)) {
            $stageMatch = [regex]::Match([string]$stageLine, '^(?<mode>[0-9]{6})\s+[0-9a-f]{40}\s+[0-3]\s+')
            if ($stageMatch.Success -and $stageMatch.Groups['mode'].Value -in @('120000', '160000')) {
                throw "$ruleId-BYPASS governed repository inventory forbids Git symlink/gitlink mode $($stageMatch.Groups['mode'].Value): $stageLine"
            }
        }
    }

    $result = [object[]]@($paths.GetEnumerator() | Sort-Object Key | ForEach-Object {
        [pscustomobject]@{ RelativePath = [string]$_.Key; FullName = [string]$_.Value }
    })
    $script:repositoryFilesCacheRoot = $rootPath
    $script:repositoryFilesCache = $result
    return $result
}

function Get-CurrentPlatformKey {
    $os = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } elseif ($IsMacOS) { 'macOS' } else { 'Unknown' }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    return "$os-$architecture"
}

function Get-CanonicalProtectedAssetPaths {
    return [string[]]@(
        '.gitattributes',
        '.github/CODEOWNERS',
        '.github/workflows/cloud-ci.yml',
        'Directory.Build.props',
        'Directory.Build.targets',
        'global.json',
        'IIoT.CloudPlatform.slnx',
        'scripts/tests/CloudTestAttributeCodec.psm1',
        'scripts/tests/TestCloudTestGovernanceBehavior.ps1',
        'scripts/tests/TestCloudTestGovernancePolicy.ps1',
        'scripts/tests/baselines/cloud-test-governance.waivers.json',
        'src/tests/Directory.Build.props',
        'src/tests/xunit.runner.json',
        'src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj',
        'src/tests/IIoT.ProductionService.Tests/IIoT.ProductionService.Tests.csproj',
        'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj',
        'src/ui/iiot-web/package.json',
        'src/ui/iiot-web/package-lock.json',
        'src/ui/iiot-web/vitest.config.ts',
        'deploy/tests/TestDeploymentPolicy.ps1',
        'deploy/tests/deployment-behavior.sh'
    )
}

function Get-FileContentManifest {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$RelativePaths
    )

    return [object[]]@($RelativePaths | Sort-Object -Unique | ForEach-Object {
        $relativePath = [string]$_
        $fullPath = Join-Path $Root $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "$ruleId-BASELINE protected asset is missing: $relativePath"
        }
        [pscustomobject][ordered]@{
            path = $relativePath
            sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}

function Get-ManifestDigest {
    param([Parameter(Mandatory)][object[]]$Entries)

    $materialLines = [string[]]@($Entries | ForEach-Object { "$([string]$_.path):$([string]$_.sha256)" })
    [Array]::Sort($materialLines, [StringComparer]::Ordinal)
    $material = $materialLines -join "`n"
    return ConvertTo-Sha256 -Value $material
}

function Get-RepositoryManifestEntries {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][scriptblock]$Predicate
    )

    $paths = @(Get-RepositoryFiles -Root $Root | Where-Object $Predicate | ForEach-Object { [string]$_.RelativePath })
    return [object[]](Get-FileContentManifest -Root $Root -RelativePaths $paths)
}

function Get-WorkflowManifestEntries {
    param([Parameter(Mandatory)][string]$Root)

    return [object[]](Get-RepositoryManifestEntries -Root $Root -Predicate {
        $_.RelativePath.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
        [IO.Path]::GetExtension($_.RelativePath) -in @('.yml', '.yaml')
    })
}

function Get-BuildControlManifestEntries {
    param([Parameter(Mandatory)][string]$Root)

    return [object[]](Get-RepositoryManifestEntries -Root $Root -Predicate {
        $fileName = [IO.Path]::GetFileName($_.RelativePath)
        $fileName -match '(?i)^Directory\.(?:Build|Solution)\.(?:props|targets)$' -or
        $fileName -match '(?i)^(?:before|after)\..+\.sln\.targets$' -or
        $fileName -match '(?i)\.[^.]*proj\.user$' -or
        $_.RelativePath -match '(?i)(?:^|/)obj/[^/]+\.[^.]*proj\..+\.(?:props|targets)$' -or
        $fileName -ieq '.editorconfig' -or
        $fileName -ieq '.globalconfig' -or
        $fileName -ieq 'runtimeconfig.template.json'
    })
}

function Get-ProjectManifestEntries {
    param([Parameter(Mandatory)][string]$Root)

    return [object[]](Get-RepositoryManifestEntries -Root $Root -Predicate {
        [IO.Path]::GetExtension($_.RelativePath) -ieq '.csproj'
    })
}

function Get-RestoreControlManifestEntries {
    param([Parameter(Mandatory)][string]$Root)

    return [object[]](Get-RepositoryManifestEntries -Root $Root -Predicate {
        $fileName = [IO.Path]::GetFileName($_.RelativePath)
        $fileName -ieq 'NuGet.Config' -or
        $fileName -ieq 'Directory.Packages.props' -or
        $fileName -ieq 'packages.lock.json' -or
        $fileName -ieq '.npmrc' -or
        $fileName -ieq 'npm-shrinkwrap.json' -or
        $fileName -ieq 'package-lock.json'
    })
}

function Get-FrontendUnitTestManifestEntries {
    param([Parameter(Mandatory)][string]$Root)

    return [object[]](Get-RepositoryManifestEntries -Root $Root -Predicate {
        $_.RelativePath.StartsWith('src/ui/iiot-web/', [StringComparison]::Ordinal) -and
        $_.RelativePath -match '(?i)\.(?:spec|test)\.ts$'
    })
}

function Get-OptionalProperty {
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object]$DefaultValue = $null
    )

    if ($null -eq $InputObject) { return $DefaultValue }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name) -and $null -ne $InputObject[$Name]) {
            return $InputObject[$Name]
        }
        return $DefaultValue
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $DefaultValue }
    return $property.Value
}

function Add-PolicyError {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Message
    )
    $Errors.Add("$Code $Message")
}

function Assert-NoPolicyErrors {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors)
    if ($Errors.Count -gt 0) {
        throw ("Cloud test governance failed:`n- " + ($Errors -join "`n- "))
    }
}

function Test-RunnerConfigurationFile {
    param(
        [Parameter(Mandatory)][string]$ResolvedRunnerConfigPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Context
    )

    $resolvedPath = Get-NormalizedPath $ResolvedRunnerConfigPath
    $runnerDirectory = Split-Path $resolvedPath -Parent
    $runnerConfigs = @(if (Test-Path $runnerDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $runnerDirectory -Force -File | Where-Object { $_.Name -match '(?i)xunit\.runner\.json$' }
    })
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne $resolvedPath) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context must contain exactly one generic xunit.runner.json; assembly-specific runner overrides are forbidden."
        return
    }
    if (-not (Test-Path $resolvedPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context does not contain the required xunit.runner.json: $ResolvedRunnerConfigPath."
        return
    }
    if ((Get-FileHash $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $xunitRunnerConfigSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json differs from the reviewed failSkips configuration."
        return
    }
    try {
        $runnerConfiguration = Get-Content $resolvedPath -Raw | ConvertFrom-Json
        if ([bool](Get-OptionalProperty $runnerConfiguration 'failSkips' $false) -ne $true) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json must set failSkips=true."
        }
    } catch {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Context xunit.runner.json is not valid JSON: $($_.Exception.Message)"
    }
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path $Path -Parent
    if (-not (Test-Path $directory)) {
        [void](New-Item $directory -ItemType Directory -Force)
    }
    $temporaryPath = Join-Path $directory ".$([System.IO.Path]::GetFileName($Path)).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 100
        [System.IO.File]::WriteAllText($temporaryPath, "$json`n", [System.Text.UTF8Encoding]::new($false))
        $null = Get-Content $temporaryPath -Raw | ConvertFrom-Json -Depth 100
        Move-Item $temporaryPath $Path -Force
    } finally {
        Remove-Item $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-Sha256 {
    param([Parameter(Mandatory)][string]$Value)
    # Hash semantic/runtime text byte-for-byte. Repository paths have a separate
    # NFC gate; normalizing identities, runner names, workflow bodies or baseline
    # text here would create collisions between ordinally distinct values.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-ActiveSdkMetadataLoadContextPath {
    $activeSdk = (& dotnet --version | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($activeSdk)) {
        throw "$ruleId-SCAN dotnet --version returned no SDK version."
    }

    $sdkDirectories = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(& dotnet --list-sdks)) {
        if ($line -match '^(?<version>\S+)\s+\[(?<root>.+)\]$' -and $Matches.version -eq $activeSdk) {
            $sdkDirectories.Add((Join-Path $Matches.root $Matches.version))
        }
    }
    foreach ($sdkDirectory in @($sdkDirectories | Select-Object -Last 1)) {
        $candidate = Join-Path $sdkDirectory 'System.Reflection.MetadataLoadContext.dll'
        if (Test-Path $candidate -PathType Leaf) {
            return (Get-NormalizedPath $candidate)
        }
    }
    throw "$ruleId-SCAN active SDK $activeSdk does not expose System.Reflection.MetadataLoadContext.dll."
}

function Test-ScannerContract {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors)

    $activeSdk = (& dotnet --version | Out-String).Trim()
    if ($activeSdk -ne $approvedDotnetSdk) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "active .NET SDK must be exactly $approvedDotnetSdk; found '$activeSdk'."
        return
    }
    try {
        $metadataLoadContextPath = Get-ActiveSdkMetadataLoadContextPath
        $actualHash = (Get-FileHash -LiteralPath $metadataLoadContextPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $platformKey = Get-CurrentPlatformKey
        $expectedHash = [string]$approvedMetadataLoadContextSha256ByPlatform[$platformKey]
        if ([string]::IsNullOrWhiteSpace($expectedHash) -or $actualHash -ne $expectedHash) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "MetadataLoadContext is not the reviewed $platformKey SDK binary: actual=$actualHash."
        }
    } catch {
        Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message $_.Exception.Message
    }
}

function Get-MetadataResolverPaths {
    param(
        [Parameter(Mandatory)][string]$TestAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    $pathsBySimpleName = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pathsBySimpleName[[System.IO.Path]::GetFileNameWithoutExtension($TestAssemblyPath)] = $TestAssemblyPath
    foreach ($referencePath in @($AdditionalReferencePaths)) {
        if ([string]::IsNullOrWhiteSpace($referencePath) -or -not (Test-Path $referencePath -PathType Leaf)) { continue }
        $simpleName = [System.IO.Path]::GetFileNameWithoutExtension($referencePath)
        if (-not $pathsBySimpleName.ContainsKey($simpleName)) {
            $pathsBySimpleName[$simpleName] = [System.IO.Path]::GetFullPath($referencePath)
        }
    }
    $assemblyDirectory = Split-Path $TestAssemblyPath -Parent
    foreach ($file in @(Get-ChildItem $assemblyDirectory -Filter '*.dll' -File -Recurse | Sort-Object { $_.DirectoryName.Length }, FullName)) {
        if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
            $pathsBySimpleName[$file.BaseName] = $file.FullName
        }
    }

    $runtimeDirectories = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @(& dotnet --list-runtimes)) {
        if ($line -match '^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<root>.+)\]$') {
            $candidate = Join-Path $Matches.root $Matches.version
            if (Test-Path $candidate -PathType Container) {
                $parsedVersion = $null
                if ([Version]::TryParse($Matches.version, [ref]$parsedVersion)) {
                    $runtimeDirectories.Add([pscustomobject]@{ Path = $candidate; Version = $parsedVersion })
                }
            }
        }
    }
    foreach ($runtime in @($runtimeDirectories | Sort-Object Version -Descending)) {
        foreach ($file in @(Get-ChildItem $runtime.Path -Filter '*.dll' -File | Sort-Object Name)) {
            if (-not $pathsBySimpleName.ContainsKey($file.BaseName)) {
                $pathsBySimpleName[$file.BaseName] = $file.FullName
            }
        }
    }

    return [string[]]@($pathsBySimpleName.Values)
}

function New-TestMetadataLoadContext {
    param(
        [Parameter(Mandatory)][string]$TestAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    $metadataLoadContextPath = Get-ActiveSdkMetadataLoadContextPath
    if ($null -eq ('System.Reflection.MetadataLoadContext' -as [type])) {
        Add-Type -Path $metadataLoadContextPath
    }
    [string[]]$resolverPaths = @(Get-MetadataResolverPaths -TestAssemblyPath $TestAssemblyPath -AdditionalReferencePaths $AdditionalReferencePaths)
    $resolver = [System.Reflection.PathAssemblyResolver]::new($resolverPaths)
    return [System.Reflection.MetadataLoadContext]::new($resolver)
}

function Test-TypeDerivesFrom {
    param(
        [AllowNull()][object]$Type,
        [Parameter(Mandatory)][string]$FullName
    )

    $current = $Type
    while ($null -ne $current) {
        if ($current.FullName -eq $FullName) { return $true }
        $current = $current.BaseType
    }
    return $false
}

function Test-IsXunitDataAttribute {
    param([Parameter(Mandatory)][object]$AttributeType)

    return (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.Sdk.DataAttribute') -or
        (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.v3.DataAttribute')
}

function Test-TypeExecutesDeclaration {
    param(
        [Parameter(Mandatory)][object]$CandidateType,
        [Parameter(Mandatory)][object]$DeclaringType
    )

    if ($CandidateType.IsAbstract) { return $false }
    if (-not $DeclaringType.IsGenericTypeDefinition) {
        return $DeclaringType.IsAssignableFrom($CandidateType)
    }

    $current = $CandidateType
    while ($null -ne $current) {
        if ($current.IsGenericType -and $current.GetGenericTypeDefinition().FullName -eq $DeclaringType.FullName) {
            return $true
        }
        $current = $current.BaseType
    }
    return $false
}

function Get-TestAttributeCategory {
    param([Parameter(Mandatory)][object]$AttributeType)

    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.TheoryAttribute') { return 'Theory' }
    if (Test-TypeDerivesFrom -Type $AttributeType -FullName 'Xunit.FactAttribute') { return 'Fact' }
    return $null
}

function Add-TraitsFromAttributes {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Attributes,
        [Parameter(Mandatory)][System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]$Traits
    )

    foreach ($attribute in $Attributes) {
        if ($attribute.AttributeType.FullName -ne 'Xunit.TraitAttribute' -or $attribute.ConstructorArguments.Count -lt 2) {
            continue
        }
        $name = [string]$attribute.ConstructorArguments[0].Value
        $value = [string]$attribute.ConstructorArguments[1].Value
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not $Traits.ContainsKey($name)) {
            $Traits[$name] = [System.Collections.Generic.List[string]]::new()
        }
        if (-not $Traits[$name].Contains($value)) {
            $Traits[$name].Add($value)
        }
    }
}

function Get-TestTraits {
    param(
        [Parameter(Mandatory)][object]$ExecutionType,
        [Parameter(Mandatory)][object]$Method
    )

    $traits = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]::new([System.StringComparer]::Ordinal)
    $currentType = $ExecutionType
    while ($null -ne $currentType -and $currentType.FullName -ne 'System.Object') {
        Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($currentType)) -Traits $traits
        $currentType = $currentType.BaseType
    }
    Add-TraitsFromAttributes -Attributes @([System.Reflection.CustomAttributeData]::GetCustomAttributes($Method)) -Traits $traits

    $ordered = [ordered]@{}
    foreach ($name in @(Get-OrdinalSortedUniqueStrings -Values @($traits.Keys))) {
        $ordered[$name] = [string[]](Get-OrdinalSortedUniqueStrings -Values @($traits[$name]))
    }
    return [pscustomobject]$ordered
}

function Get-MethodParameterSignature {
    param([Parameter(Mandatory)][object]$Method)
    return (@($Method.GetParameters() | ForEach-Object { $_.ParameterType.ToString() }) -join ',')
}

function Get-TestAttributePolicy {
    param([Parameter(Mandatory)][object]$Attribute)

    $values = [ordered]@{
        Skip = ''
        Explicit = $false
        SkipWhen = ''
        SkipUnless = ''
        SkipType = ''
        SkipExceptions = ''
        Timeout = 0
    }
    foreach ($argument in @($Attribute.NamedArguments)) {
        if (-not $values.Contains($argument.MemberName)) { continue }
        $value = $argument.TypedValue.Value
        $values[$argument.MemberName] = if ($null -eq $value) { '' } else { [string]$value }
    }

    $isDisabled = -not [string]::IsNullOrWhiteSpace([string]$values.Skip) -or
        [string]$values.Explicit -eq 'True' -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipWhen) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipUnless) -or
        -not [string]::IsNullOrWhiteSpace([string]$values.SkipExceptions)
    $signature = @($values.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '|'
    return [pscustomobject][ordered]@{
        signature = $signature
        isDisabled = $isDisabled
        skip = [string]$values.Skip
        explicit = [string]$values.Explicit -eq 'True'
        skipWhen = [string]$values.SkipWhen
        skipUnless = [string]$values.SkipUnless
        skipType = [string]$values.SkipType
        skipExceptions = [string]$values.SkipExceptions
        timeout = [int]$values.Timeout
    }
}

function Get-TestAssemblySnapshot {
    param(
        [Parameter(Mandatory)][string]$ResolvedProjectPath,
        [Parameter(Mandatory)][string]$ResolvedProjectName,
        [Parameter(Mandatory)][string]$ResolvedAssemblyPath,
        [string[]]$AdditionalReferencePaths = @()
    )

    if (-not (Test-Path $ResolvedAssemblyPath -PathType Leaf)) {
        throw "$ruleId-SCAN test assembly does not exist: $ResolvedAssemblyPath"
    }

    $context = New-TestMetadataLoadContext -TestAssemblyPath $ResolvedAssemblyPath -AdditionalReferencePaths $AdditionalReferencePaths
    $tests = [System.Collections.Generic.List[object]]::new()
    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    try {
        $assembly = $context.LoadFromAssemblyPath($ResolvedAssemblyPath)
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        $allTypes = @($assembly.GetTypes() | Sort-Object FullName)
        foreach ($declaringType in $allTypes) {
            $bindingFlags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
            foreach ($method in @($declaringType.GetMethods($bindingFlags) | Sort-Object Name, MetadataToken)) {
                if ($method.IsSpecialName) { continue }
                $methodAttributes = @([System.Reflection.CustomAttributeData]::GetCustomAttributes($method))
                $testAttributes = @($methodAttributes | Where-Object { $null -ne (Get-TestAttributeCategory -AttributeType $_.AttributeType) })
                if ($testAttributes.Count -eq 0) { continue }
                if ($testAttributes.Count -ne 1) {
                    throw "$ruleId-SCAN $($declaringType.FullName).$($method.Name) has $($testAttributes.Count) Fact/Theory-derived attributes."
                }

                $testAttribute = $testAttributes[0]
                $attributeCategory = Get-TestAttributeCategory -AttributeType $testAttribute.AttributeType
                $parameterSignature = Get-MethodParameterSignature -Method $method
                $declaringTypeName = [string]$method.DeclaringType.FullName
                $testAttributeType = [string]$testAttribute.AttributeType.FullName
                if (-not (Test-ContainsOrdinal -Values $allowedTestAttributeTypes -Candidate $testAttributeType)) {
                    throw "$ruleId-SCAN unregistered Fact/Theory-derived attribute '$testAttributeType' on $declaringTypeName.$($method.Name)."
                }
                $genericArity = @($method.GetGenericArguments()).Count
                $symbol = "$declaringTypeName.$($method.Name)($parameterSignature)"
                $logicalKey = "cloud-test-decl-v1|$declaringTypeName|$($method.Name)``$genericArity|$parameterSignature"
                $logicalId = "cloud-test-decl-v1:$(ConvertTo-Sha256 $logicalKey)"
                $physicalKey = "cloud-test-physical-v1|$relativeProjectPath|$logicalId"
                $id = "cloud-test-physical-v1:$(ConvertTo-Sha256 $physicalKey)"
                if (-not $seenIds.Add($id)) {
                    throw "$ruleId-SCAN duplicate test identity '$id' in $ResolvedAssemblyPath."
                }

                $inlineDataAttributes = @($methodAttributes | Where-Object { $_.AttributeType.FullName -eq 'Xunit.InlineDataAttribute' })
                $inlineDataRows = $inlineDataAttributes.Count
                $inlineDataSignatures = @($inlineDataAttributes | ForEach-Object {
                    Get-CloudTestCustomAttributeSignature -Attribute $_
                } | Sort-Object)
                $dynamicDataSources = @($methodAttributes |
                    Where-Object {
                        (Test-IsXunitDataAttribute -AttributeType $_.AttributeType) -and
                        $_.AttributeType.FullName -ne 'Xunit.InlineDataAttribute'
                    } |
                    ForEach-Object { Get-CloudTestCustomAttributeSignature -Attribute $_ } |
                    Sort-Object -Unique)
                $rowProjection = if ($attributeCategory -eq 'Theory' -and $inlineDataRows -gt 0) { $inlineDataRows } else { 1 }
                $executionTypes = [System.Collections.Generic.List[object]]::new()
                $executionCandidates = if ($method.IsStatic) {
                    @($declaringType)
                } else {
                    @($allTypes | Where-Object { Test-TypeExecutesDeclaration -CandidateType $_ -DeclaringType $declaringType })
                }
                foreach ($executionType in $executionCandidates) {
                    $executionTypeName = [string]$executionType.FullName
                    $executionKey = "cloud-test-execution-v1|$relativeProjectPath|$executionTypeName|$logicalId"
                    $executionTypes.Add([pscustomobject][ordered]@{
                        id = "cloud-test-execution-v1:$(ConvertTo-Sha256 $executionKey)"
                        name = $executionTypeName
                        traits = Get-TestTraits -ExecutionType $executionType -Method $method
                    })
                }
                if ($executionTypes.Count -eq 0) {
                    throw "$ruleId-SCAN $symbol has no concrete execution type and would not be discovered by the required runner."
                }
                $attributePolicy = Get-TestAttributePolicy -Attribute $testAttribute
                $dataAttributePolicies = @($methodAttributes |
                    Where-Object { Test-IsXunitDataAttribute -AttributeType $_.AttributeType } |
                    ForEach-Object { Get-TestAttributePolicy -Attribute $_ })
                $dataPolicySignature = @($dataAttributePolicies | ForEach-Object { $_.signature } | Sort-Object) -join '||'
                $attributePolicy.signature = "$($attributePolicy.signature)|DataPolicies=$dataPolicySignature"
                $attributePolicy.isDisabled = [bool]$attributePolicy.isDisabled -or @($dataAttributePolicies | Where-Object { $_.isDisabled }).Count -gt 0

                $tests.Add([pscustomobject][ordered]@{
                    id = $id
                    logicalId = $logicalId
                    symbol = $symbol
                    executionType = $declaringTypeName
                    declaringType = $declaringTypeName
                    methodName = [string]$method.Name
                    parameterSignature = $parameterSignature
                    attributeCategory = $attributeCategory
                    testAttributeType = $testAttributeType
                    testAttributePolicy = $attributePolicy
                    inlineDataRows = $inlineDataRows
                    inlineDataSignatures = [string[]]$inlineDataSignatures
                    dynamicDataSources = [string[]]$dynamicDataSources
                    executionTypes = [object[]]@($executionTypes | Sort-Object name)
                    projectedCases = $rowProjection * $executionTypes.Count
                    traits = Get-TestTraits -ExecutionType $declaringType -Method $method
                })
            }
        }
    } finally {
        $context.Dispose()
    }

    return [pscustomobject][ordered]@{
        projectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedProjectPath
        projectName = $ResolvedProjectName
        assemblyPath = Get-RelativePath -BasePath $RepositoryRoot -Path $ResolvedAssemblyPath
        assemblySha256 = (Get-FileHash $ResolvedAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        declarations = $tests.Count
        executionTemplates = [int](($tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum)
        projectedCases = [int](($tests | Measure-Object -Property projectedCases -Sum).Sum)
        tests = [object[]]@($tests | Sort-Object id)
    }
}

function Get-TestProjectSpecifications {
    param(
        [Parameter(Mandatory)][string]$RequestedConfiguration,
        [switch]$AllowMissingAssembly
    )

    $specifications = [System.Collections.Generic.List[object]]::new()
    $testRoot = Get-NormalizedPath (Join-Path $RepositoryRoot 'src/tests')
    foreach ($projectEntry in @(Get-RepositoryFiles -Root $RepositoryRoot | Where-Object {
        $_.FullName.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and
        [string]::Equals([IO.Path]::GetExtension($_.FullName), '.csproj', [StringComparison]::OrdinalIgnoreCase)
    } | Sort-Object RelativePath)) {
        $projectFile = Get-Item -LiteralPath $projectEntry.FullName -Force
        [xml]$projectXml = Get-Content $projectFile.FullName -Raw
        $isTestProject = @($projectXml.SelectNodes('/Project/PropertyGroup/IsTestProject') | Where-Object { [string]$_.InnerText -eq 'true' }).Count -gt 0
        if (-not $isTestProject) { continue }
        $projectNameValue = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
        $assemblyNameValue = @($projectXml.SelectNodes('/Project/PropertyGroup/AssemblyName') | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($assemblyNameValue.Count -eq 0) { $assemblyNameValue = @($projectNameValue) }
        $targetFramework = @($projectXml.SelectNodes('/Project/PropertyGroup/TargetFramework') | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Last 1)
        if ($targetFramework.Count -ne 1) {
            throw "$ruleId-BASELINE cannot resolve one TargetFramework from $($projectFile.FullName)."
        }
        $resolvedAssembly = Join-Path $projectFile.Directory.FullName "bin/$RequestedConfiguration/$($targetFramework[0])/$($assemblyNameValue[0]).dll"
        if (-not $AllowMissingAssembly -and -not (Test-Path $resolvedAssembly -PathType Leaf)) {
            throw "$ruleId-SCAN build $($projectFile.FullName) for $RequestedConfiguration before running this mode. Missing: $resolvedAssembly"
        }
        $specifications.Add([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $projectFile.FullName
            ProjectName = $projectNameValue
            AssemblyPath = Get-NormalizedPath $resolvedAssembly
            RunnerConfigPath = Get-NormalizedPath (Join-Path (Split-Path $resolvedAssembly -Parent) 'xunit.runner.json')
        })
    }
    return [object[]]@($specifications)
}

function Get-ProjectSourceFiles {
    param([Parameter(Mandatory)][string]$ResolvedProjectPath)

    $projectDirectory = Split-Path $ResolvedProjectPath -Parent
    $normalizedProjectDirectory = Get-NormalizedPath $projectDirectory
    return [string[]]@(Get-RepositoryFiles -Root $RepositoryRoot |
        Where-Object {
            $_.FullName.StartsWith("$normalizedProjectDirectory/", [StringComparison]::Ordinal) -and
            [string]::Equals([IO.Path]::GetExtension($_.FullName), '.cs', [StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object { [string]$_.RelativePath } |
        Sort-Object -Unique)
}

function Get-CanonicalRequiredCommandPrefixes {
    return [string[]]@(
        './scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateBaselineAnchor',
        './scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateStatic',
        './scripts/tests/TestCloudTestGovernanceBehavior.ps1',
        'dotnet restore IIoT.CloudPlatform.slnx --disable-build-servers --nologo -noAutoResponse',
        'dotnet build IIoT.CloudPlatform.slnx -c Release --no-restore --disable-build-servers --nologo -noAutoResponse',
        './scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateRepository',
        './scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateDiscovery',
        'dotnet test src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse',
        'dotnet test src/tests/IIoT.ProductionService.Tests/IIoT.ProductionService.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse',
        'dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj -c Release --no-build --filter FullyQualifiedName~ClientReleaseCommitRecoveryPostgresTests --no-restore --disable-build-servers --nologo -noAutoResponse',
        'dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse --filter ConfigurationGuardTests',
        'dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse --filter DeploymentGuardTests',
        'bash deploy/tests/deployment-behavior.sh',
        'npm run test:unit',
        'npm run build'
    )
}

function Get-CanonicalWorkflowRunSteps {
    return [object[]]@(
        [pscustomobject]@{
            Name = 'Validate immutable test baseline anchor'
            Shell = 'pwsh'
            Run = @'
$eventName = '${{ github.event_name }}'
$eventRef = '${{ github.ref }}'
if ($eventName -eq 'workflow_dispatch' -and $eventRef -ne 'refs/heads/main') {
  throw 'workflow_dispatch is trusted only on refs/heads/main.'
}
$trustedBase = '${{ github.event.pull_request.base.sha || github.event.before }}'
if ($trustedBase -notmatch '^[0-9a-fA-F]{40}$' -or $trustedBase -match '^0{40}$') {
  $trustedBase = (git rev-parse HEAD^ | Out-String).Trim()
}
./scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateBaselineAnchor -TrustedBaseRevision $trustedBase
'@
        },
        [pscustomobject]@{
            Name = 'Validate reviewed restore and build inputs'
            Shell = 'pwsh'
            Run = './scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release'
        },
        [pscustomobject]@{
            Name = 'Run Cloud test governance self-tests'
            Shell = 'pwsh'
            Run = './scripts/tests/TestCloudTestGovernanceBehavior.ps1'
        },
        [pscustomobject]@{
            Name = 'Restore cloud solution'
            Shell = ''
            Run = 'dotnet restore IIoT.CloudPlatform.slnx --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Build cloud solution'
            Shell = ''
            Run = 'dotnet build IIoT.CloudPlatform.slnx -c Release --no-restore --disable-build-servers --nologo -noAutoResponse'
        },
        [pscustomobject]@{
            Name = 'Validate Cloud test repository and discovery'
            Shell = 'pwsh'
            Run = "./scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release`n./scripts/tests/TestCloudTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release"
        }
    )
}

function Get-WorkflowRunSteps {
    param([Parameter(Mandatory)][string]$WorkflowContent)

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $steps = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $nameMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)-\s+name:\s*(?<name>.+?)\s*$')
        if (-not $nameMatch.Success) { continue }
        $stepIndent = $nameMatch.Groups['indent'].Value.Length
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextStep = [regex]::Match($lines[$end], '^(?<indent>\s*)-\s+')
            if ($nextStep.Success -and $nextStep.Groups['indent'].Value.Length -eq $stepIndent) { break }
            $end++
        }

        $run = $null
        $shell = $null
        $hasIf = $false
        $ambiguous = $false
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $unexpectedKeys = [System.Collections.Generic.List[string]]::new()
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($stepIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            if (-not $seenDirectKeys.Add($directKey)) {
                $ambiguous = $true
                continue
            }
            if ($directKey -notin @('run', 'shell')) {
                $unexpectedKeys.Add($directKey)
            }
            switch ($directKey) {
                'shell' { $shell = $propertyMatch.Groups['value'].Value.Trim() }
                'if' { $hasIf = $true }
                'run' {
                    $value = $propertyMatch.Groups['value'].Value.Trim()
                    if ($value -ne '|') {
                        $run = $value
                        continue
                    }
                    $runIndent = $propertyMatch.Groups['indent'].Value.Length
                    $blockLines = [System.Collections.Generic.List[string]]::new()
                    $blockCursor = $cursor + 1
                    while ($blockCursor -lt $end) {
                        $line = $lines[$blockCursor]
                        $leading = [regex]::Match($line, '^\s*').Value.Length
                        if (-not [string]::IsNullOrWhiteSpace($line) -and $leading -le $runIndent) { break }
                        $blockLines.Add($line)
                        $blockCursor++
                    }
                    while ($blockLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($blockLines[$blockLines.Count - 1])) {
                        $blockLines.RemoveAt($blockLines.Count - 1)
                    }
                    $contentIndent = @($blockLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { [regex]::Match($_, '^\s*').Value.Length } | Measure-Object -Minimum).Minimum
                    $run = (@($blockLines | ForEach-Object {
                        if ([string]::IsNullOrWhiteSpace($_)) { return '' }
                        return $_.Substring([int]$contentIndent).TrimEnd()
                    }) -join "`n")
                }
            }
        }
        $name = $nameMatch.Groups['name'].Value.Trim().Trim('"', "'")
        $steps.Add([pscustomobject]@{ Name = $name; Run = $run; Shell = $shell; HasIf = $hasIf; Ambiguous = $ambiguous; UnexpectedKeys = [string[]]$unexpectedKeys })
        $index = $end - 1
    }
    return [object[]]@($steps)
}

function Test-CanonicalWorkflowRunSteps {
    param(
        [Parameter(Mandatory)][string]$JobContent,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [string]$Context = '.github/workflows/cloud-ci.yml build-test'
    )

    $actualSteps = @(Get-WorkflowRunSteps -WorkflowContent $JobContent)
    $lastIndex = -1
    foreach ($expected in @(Get-CanonicalWorkflowRunSteps)) {
        $matches = @()
        for ($index = 0; $index -lt $actualSteps.Count; $index++) {
            if ([string]::Equals([string]$actualSteps[$index].Name, [string]$expected.Name, [StringComparison]::Ordinal)) {
                $matches += [pscustomobject]@{ Index = $index; Step = $actualSteps[$index] }
            }
        }
        if ($matches.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI-STEP" -Message "$Context must contain exactly one canonical '$($expected.Name)' step; found $($matches.Count)."
            continue
        }
        $match = $matches[0]
        $actual = $match.Step
        if ($match.Index -le $lastIndex) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI-STEP" -Message "$Context schedules '$($expected.Name)' outside the canonical trust/execution order."
        }
        $lastIndex = $match.Index
        $actualShell = if ($null -eq $actual.Shell) { '' } else { [string]$actual.Shell }
        if ($actual.Ambiguous -or $actual.HasIf -or @($actual.UnexpectedKeys).Count -gt 0 -or
            $actualShell -cne [string]$expected.Shell -or
            [string]$actual.Run -cne ([string]$expected.Run).TrimEnd()) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI-STEP" -Message "$Context step '$($expected.Name)' must preserve its exact run body/shell and cannot add if/duplicate/unexpected direct keys."
        }
    }
}

function Get-WorkflowJobEnvelope {
    param(
        [Parameter(Mandatory)][string]$WorkflowContent,
        [Parameter(Mandatory)][string]$JobName
    )

    $lines = [regex]::Split($WorkflowContent.Replace("`r`n", "`n"), "`n")
    $matches = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $jobMatch = [regex]::Match($lines[$index], '^(?<indent>\s*)' + [regex]::Escape($JobName) + ':\s*$')
        if (-not $jobMatch.Success -or $jobMatch.Groups['indent'].Value.Length -ne 2) { continue }
        $jobIndent = 2
        $end = $index + 1
        while ($end -lt $lines.Count) {
            $nextJob = [regex]::Match($lines[$end], '^(?<indent>\s*)[A-Za-z0-9_-]+:\s*$')
            if ($nextJob.Success -and $nextJob.Groups['indent'].Value.Length -eq $jobIndent) { break }
            $end++
        }
        $timeoutValues = [System.Collections.Generic.List[string]]::new()
        $runsOnValues = [System.Collections.Generic.List[string]]::new()
        $directKeys = [System.Collections.Generic.List[string]]::new()
        $seenDirectKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $ambiguous = $false
        $hasIf = $false
        for ($cursor = $index + 1; $cursor -lt $end; $cursor++) {
            $propertyMatch = [regex]::Match($lines[$cursor], '^(?<indent>\s*)(?<name>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $propertyMatch.Success -or $propertyMatch.Groups['indent'].Value.Length -ne ($jobIndent + 2)) { continue }
            $directKey = $propertyMatch.Groups['name'].Value
            $directKeys.Add($directKey)
            if (-not $seenDirectKeys.Add($directKey)) { $ambiguous = $true }
            if ($directKey -eq 'timeout-minutes') {
                $timeoutValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'runs-on') {
                $runsOnValues.Add($propertyMatch.Groups['value'].Value.Trim())
            } elseif ($directKey -eq 'if') {
                $hasIf = $true
            }
        }
        $unexpectedKeys = @($directKeys | Where-Object { $_ -notin @('runs-on', 'timeout-minutes', 'steps') } | Sort-Object -Unique)
        $jobContent = @($lines[$index..($end - 1)]) -join "`n"
        $matches.Add([pscustomobject]@{
            TimeoutValues = [string[]]$timeoutValues
            RunsOnValues = [string[]]$runsOnValues
            HasIf = $hasIf
            Ambiguous = $ambiguous
            UnexpectedKeys = [string[]]$unexpectedKeys
            Content = $jobContent
        })
    }
    return [object[]]@($matches)
}

function Get-CanonicalWorkflowStepNames {
    param([Parameter(Mandatory)][string]$WorkflowPath)

    return [string[]]@(
        'Checkout',
        'Validate immutable test baseline anchor',
        'Setup .NET',
        'Verify .NET SDK',
        'Validate reviewed restore and build inputs',
        'Run Cloud test governance self-tests',
        'Setup Node',
        'Create test evidence directory',
        'Restore cloud solution',
        'Restore web dependencies',
        'Enforce incremental deployment policy',
        'Build cloud solution',
        'Validate Cloud test repository and discovery',
        'Run service layer tests',
        'Run production service tests',
        'Run client release PostgreSQL post-commit recovery test',
        'Run configuration guard tests',
        'Run deployment guard tests',
        'Validate deploy script syntax',
        'Run deployment behavior tests',
        'Test and build web app',
        'Validate production compose',
        'Reconcile required test results',
        'Upload required test evidence'
    )
}

function Get-WorkflowStepNames {
    param([Parameter(Mandatory)][string]$JobContent)

    $lines = [regex]::Split($JobContent.Replace("`r`n", "`n"), "`n")
    $stepsMarkers = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $stepMatch = [regex]::Match($line, '^\s{6}-\s+(?<body>.+)$')
        if (-not $stepMatch.Success) { continue }
        $nameMatch = [regex]::Match($stepMatch.Groups['body'].Value, '^name:\s*(?<name>.+?)\s*$')
        if ($nameMatch.Success) {
            $stepsMarkers.Add($nameMatch.Groups['name'].Value.Trim().Trim('"', "'"))
        } else {
            $stepsMarkers.Add('<unnamed>')
        }
    }
    return [string[]]$stepsMarkers
}

function Test-WorkflowDocumentSemantics {
    param(
        [Parameter(Mandatory)][string]$WorkflowContent,
        [Parameter(Mandatory)][object]$Requirement,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [string]$Context = '.github/workflows/cloud-ci.yml'
    )

    $content = $WorkflowContent.Replace("`r`n", "`n").Replace('\', '/')
    $jobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $content -JobName 'build-test')
    if ($jobEnvelopes.Count -ne 1 -or $jobEnvelopes[0].HasIf -or $jobEnvelopes[0].Ambiguous -or
        @($jobEnvelopes[0].UnexpectedKeys).Count -gt 0 -or
        @($jobEnvelopes[0].RunsOnValues).Count -ne 1 -or [string]$jobEnvelopes[0].RunsOnValues[0] -cne 'ubuntu-24.04' -or
        @($jobEnvelopes[0].TimeoutValues).Count -ne 1 -or [string]$jobEnvelopes[0].TimeoutValues[0] -cne '25') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context job 'build-test' must be an unconditional ubuntu-24.04 job with exact direct properties and timeout-minutes: 25."
    }
    if ($jobEnvelopes.Count -eq 1) {
        Test-CanonicalWorkflowRunSteps -JobContent ([string]$jobEnvelopes[0].Content) -Errors $Errors -Context "$Context build-test"
        $actualStepNames = @(Get-WorkflowStepNames -JobContent ([string]$jobEnvelopes[0].Content))
        $expectedStepNames = @(Get-CanonicalWorkflowStepNames -WorkflowPath $Context)
        if (($actualStepNames -join '|') -cne ($expectedStepNames -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context required job step names/order differ from the exact reviewed sequence."
        }
    }
    $manualJobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $content -JobName 'full-end-to-end')
    if ($manualJobEnvelopes.Count -ne 1 -or
        -not ([string]$manualJobEnvelopes[0].Content).Contains("`n    needs: build-test`n", [StringComparison]::Ordinal)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI-ORDER" -Message "$Context manual full-end-to-end job must wait for the trusted build-test gate before any restore/build/test execution."
    }
    if ($content -match '(?mi)^\s*continue-on-error:\s*true\s*$' -or
        $content -match '(?mi)^\s*if:\s*(?:false|\$\{\{\s*false\s*\}\})\s*$' -or
        $content -match '(?mi)^\s*pull_request_target\s*:' -or
        $content -match '(?mi)^\s*permissions:\s*(?:write-all|write)\s*$' -or
        $content -match '(?mi)^\s*shell:\s*bash\s+\{0\}\s*$' -or
        $content -match '(?m)(?:\|\|\s*true\s*$|^\s*set\s+\+e\s*$)') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI-SECURITY" -Message "$Context weakens trigger, permissions, shell fail-fast, or command failure semantics."
    }
    foreach ($usesMatch in [regex]::Matches($content, '(?m)^\s*uses:\s*(?<value>[^\s#]+)')) {
        $usesValue = $usesMatch.Groups['value'].Value
        if ($usesValue -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI-ACTION" -Message "$Context uses movable or non-canonical Action reference '$usesValue'."
        }
    }
    if (-not $content.Contains('fetch-depth: 0', [StringComparison]::Ordinal) -or
        -not $content.Contains('ref: ${{ github.event.pull_request.head.sha || github.sha }}', [StringComparison]::Ordinal)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context must checkout full history at the pull-request head/push SHA so the trusted-base relationship is evaluated against the candidate commit, not a synthetic merge commit."
    }
    foreach ($dotnetLine in [regex]::Matches($content, '(?m)^\s*(?<command>dotnet\s+(?:restore|build|test)\b[^\r\n]*)$')) {
        if ($dotnetLine.Groups['command'].Value -notmatch '(?:^|\s)-noAutoResponse(?:\s|$)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-RESPONSE" -Message "$Context dotnet command does not disable automatic response files: $($dotnetLine.Groups['command'].Value.Trim())."
        }
    }
    if ($content -match '(?i)(?:IsTestProject|ImportDirectoryBuildTargets)\s*=\s*false' -or
        $content -match '(?i)(?:DirectoryBuildTargetsPath|DesignTimeBuild)\s*=' -or
        $content -match '(?i)(?:--settings(?:\s|=)|RunSettingsFilePath|\.runsettings)') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context configures an MSBuild/VSTest bypass."
    }
    if ($content -notmatch '(?m)^\s*pull_request:\s*\{\}\s*$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context must emit the required status for every pull request; path-filtered pull_request triggers are forbidden."
    }
    if ($content -match '(?m)^\s{4}paths(?:-ignore)?:\s*$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI-TRIGGER" -Message "$Context main push trigger must not use path filters; new root restore/build inputs must always emit the required check."
    }
    foreach ($requiredProject in @($Requirement.requiredTestProjects)) {
        $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?dotnet[ \t]+test[ \t]+' + [regex]::Escape([string]$requiredProject) + '(?=[ \t]|$)'
        if ([regex]::Matches($content, $pattern).Count -eq 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context does not schedule '$requiredProject'."
        }
    }
    $lastCommandPosition = -1
    foreach ($commandPrefix in @($Requirement.requiredCommandPrefixes)) {
        $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?' + [regex]::Escape([string]$commandPrefix) + '(?=[ \t]|$)'
        $match = [regex]::Match($content, $pattern)
        if (-not $match.Success) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context is missing required command '$commandPrefix'."
        }
        elseif ($match.Index -le $lastCommandPosition) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$Context schedules '$commandPrefix' out of governance order."
        }
        else {
            $lastCommandPosition = $match.Index
        }
    }
}

function Get-GeneratedProjectPolicy {
    param(
        [Parameter(Mandatory)][string]$GeneratedProjectName,
        [string]$GeneratedProjectPath
    )

    $freezeMode = 'None'
    $frozenTypePatterns = @()
    $allowedNewTestKinds = @()
    $allowedNewRuntimes = @()
    $forbiddenNewTestKinds = @()
    $discoveryCeilings = @()
    $frozenSourceFiles = @()
    $frozenSourceHashes = [ordered]@{}
    $protectBaselineRemovals = $true

    if ($GeneratedProjectName -eq 'IIoT.EndToEndTests') {
        $freezeMode = 'All'
        $frozenTypePatterns = @(
            '*.ConfigurationGuardTests',
            '*.RateLimitingConfigurationGuardTests',
            '*.ClientReleaseUploadBoundaryTests',
            '*.DeploymentGuardTests'
        )
        $allowedNewTestKinds = @('Integration', 'EndToEnd', 'Contract', 'Persistence', 'Workflow', 'Security')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'LiveExternal')
        $forbiddenNewTestKinds = @('Architecture', 'Deployment', 'UI', 'GoldenEval', 'Performance', 'SoakChaos')
        $discoveryCeilings = @(
            [pscustomobject]@{ displayNameContains = 'IIoT.EndToEndTests.ConfigurationGuardTests.'; maximum = 78 },
            [pscustomobject]@{ displayNameContains = 'IIoT.EndToEndTests.RateLimitingConfigurationGuardTests.'; maximum = 3 },
            [pscustomobject]@{ displayNameContains = 'IIoT.EndToEndTests.ClientReleaseUploadBoundaryTests.'; maximum = 5 },
            [pscustomobject]@{ displayNameContains = 'IIoT.EndToEndTests.DeploymentGuardTests.'; maximum = 20 }
        )
        if (-not [string]::IsNullOrWhiteSpace($GeneratedProjectPath)) {
            $frozenSourceFiles = Get-ProjectSourceFiles -ResolvedProjectPath $GeneratedProjectPath
            foreach ($relativeSource in $frozenSourceFiles) {
                $frozenSourceHashes[$relativeSource] = (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $relativeSource) -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    } elseif ($GeneratedProjectName -eq 'IIoT.ProductionService.Tests') {
        $freezeMode = 'Structure'
        $allowedNewTestKinds = @('Unit', 'Aggregate', 'Application', 'Workflow', 'Contract', 'Persistence', 'Security')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite')
        $forbiddenNewTestKinds = @('Architecture', 'Deployment', 'UI', 'GoldenEval', 'Performance', 'SoakChaos')
    } elseif ($GeneratedProjectName -eq 'IIoT.ServiceLayer.Tests') {
        $freezeMode = 'Structure'
        $allowedNewTestKinds = @('Unit', 'Aggregate', 'Application', 'Workflow', 'Contract', 'Persistence', 'Integration', 'Security')
        $allowedNewRuntimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ')
        $forbiddenNewTestKinds = @('Architecture', 'Deployment', 'UI', 'GoldenEval', 'Performance', 'SoakChaos')
    } else {
        throw "$ruleId-ROUTE unreviewed test project '$GeneratedProjectName'."
    }

    return [pscustomobject][ordered]@{
        isLegacy = $true
        freezeMode = $freezeMode
        frozenTypePatterns = [string[]]$frozenTypePatterns
        allowedNewTestKinds = [string[]]$allowedNewTestKinds
        allowedNewRuntimes = [string[]]$allowedNewRuntimes
        forbiddenNewTestKinds = [string[]]$forbiddenNewTestKinds
        discoveryCeilings = [object[]]$discoveryCeilings
        frozenSourceFiles = [string[]]$frozenSourceFiles
        frozenSourceHashes = [pscustomobject]$frozenSourceHashes
        protectBaselineRemovals = $protectBaselineRemovals
    }
}

function Get-TraitValues {
    param(
        [AllowNull()][object]$Traits,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Traits) { return @() }
    if ($Traits -is [System.Collections.IDictionary]) {
        $matchingKey = @($Traits.Keys | Where-Object { [string]::Equals([string]$_, $Name, [StringComparison]::Ordinal) })
        if ($matchingKey.Count -ne 1) { return @() }
        return [string[]]@(Get-OrdinalSortedUniqueStrings -Values @($Traits[$matchingKey[0]]))
    }
    $matchingProperty = @($Traits.PSObject.Properties | Where-Object {
        [string]::Equals([string]$_.Name, $Name, [StringComparison]::Ordinal)
    })
    if ($matchingProperty.Count -ne 1) { return @() }
    return [string[]]@(Get-OrdinalSortedUniqueStrings -Values @($matchingProperty[0].Value))
}

function Get-TraitNames {
    param([AllowNull()][object]$Traits)

    if ($null -eq $Traits) { return [string[]]@() }
    $names = if ($Traits -is [System.Collections.IDictionary]) {
        @($Traits.Keys | ForEach-Object { [string]$_ })
    } else {
        @($Traits.PSObject.Properties | ForEach-Object { [string]$_.Name })
    }
    return [string[]](Get-OrdinalSortedUniqueStrings -Values $names)
}

function Get-OrdinalSortedUniqueStrings {
    param([AllowEmptyCollection()][object[]]$Values = @())

    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        [void]$set.Add([string]$value)
    }
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $set) { $result.Add($value) }
    $result.Sort([StringComparer]::Ordinal)
    return [string[]]$result
}

function Test-ContainsOrdinal {
    param(
        [AllowEmptyCollection()][object[]]$Values = @(),
        [Parameter(Mandatory)][AllowEmptyString()][string]$Candidate
    )

    foreach ($value in @($Values)) {
        if ([string]::Equals([string]$value, $Candidate, [StringComparison]::Ordinal)) {
            return $true
        }
    }
    return $false
}

function Get-TraitMapSignature {
    param([AllowNull()][object]$Traits)

    if ($null -eq $Traits) { return '' }
    $names = if ($Traits -is [System.Collections.IDictionary]) {
        @(Get-OrdinalSortedUniqueStrings -Values @($Traits.Keys))
    } else {
        @(Get-OrdinalSortedUniqueStrings -Values @($Traits.PSObject.Properties | ForEach-Object { [string]$_.Name }))
    }
    return (@($names | ForEach-Object {
        $name = $_
        $values = @(Get-TraitValues -Traits $Traits -Name $name)
        "$name=$($values -join ',')"
    }) -join ';')
}

function Get-TestTraitSignature {
    param([Parameter(Mandatory)][object]$Test)

    $executionTraits = @((Get-OptionalProperty $Test 'executionTypes' @()) |
        Sort-Object name |
        ForEach-Object {
            $executionType = $_
            $executionTraitSignature = Get-TraitMapSignature -Traits $executionType.traits
            "$([string]$executionType.name):$executionTraitSignature"
        }) -join '||'
    return "Declaration=$(Get-TraitMapSignature -Traits $Test.traits)|Executions=$executionTraits"
}

function Test-NewTestMetadata {
    param(
        [Parameter(Mandatory)][object]$Test,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Location
    )

    if (-not (Test-ContainsOrdinal -Values $allowedTestAttributeTypes -Candidate ([string]$Test.testAttributeType))) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "$Location uses unregistered test attribute '$($Test.testAttributeType)'."
    }
    if ([string]$Test.testAttributeType -eq 'IIoT.EndToEndTests.WorkspaceAlignmentFactAttribute') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WORKSPACE-ALIGNMENT" -Message "$Location attempts to add a second WorkspaceAlignmentFact test."
    }
    if (@(Get-OptionalProperty $Test 'dynamicDataSources' @()).Count -gt 0) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Location uses dynamic MemberData/ClassData; Phase 0 requires deterministic InlineData projection."
    }

    $governedTraitNames = @('TestKind', 'Runtime', 'Risk', 'Capability', 'Owner', 'RegressionId')
    foreach ($actualTraitName in @(Get-TraitNames -Traits $Test.traits)) {
        $caseInsensitiveMatch = @($governedTraitNames | Where-Object {
            [string]::Equals([string]$_, $actualTraitName, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($caseInsensitiveMatch.Count -eq 1 -and
            -not [string]::Equals([string]$caseInsensitiveMatch[0], $actualTraitName, [StringComparison]::Ordinal)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location uses non-canonical trait key '$actualTraitName'; expected '$($caseInsensitiveMatch[0])'."
        }
    }

    $singleValueTraits = @('TestKind', 'Risk', 'Owner')
    $multiValueTraits = @('Capability', 'Runtime')
    foreach ($name in $singleValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires exactly one $name trait; found $($values.Count)."
        }
    }
    foreach ($name in $multiValueTraits) {
        $values = @(Get-TraitValues -Traits $Test.traits -Name $name)
        if ($values.Count -lt 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location requires at least one $name trait."
        }
    }

    $testKind = @(Get-TraitValues -Traits $Test.traits -Name 'TestKind')
    $runtime = @(Get-TraitValues -Traits $Test.traits -Name 'Runtime')
    $risk = @(Get-TraitValues -Traits $Test.traits -Name 'Risk')
    if ([bool](Get-OptionalProperty (Get-OptionalProperty $Test 'testAttributePolicy' $null) 'isDisabled' $false)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message "$Location is Skip/Explicit/conditional-skip and cannot enter a required Cloud test lane."
    }
    if ($testKind.Count -eq 1 -and -not (Test-ContainsOrdinal -Values @($Baseline.allowedMetadata.testKinds) -Candidate $testKind[0])) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported TestKind '$($testKind[0])'."
    }
    if ($testKind.Count -eq 1 -and $testKind[0] -match '^(?:Regression|NonUi|General|Misc|Phase.*|Batch.*)$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location uses forbidden legacy TestKind '$($testKind[0])'."
    }
    foreach ($value in $runtime) {
        if (-not (Test-ContainsOrdinal -Values @($Baseline.allowedMetadata.runtimes) -Candidate $value)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Runtime '$value'."
        }
    }
    if ($risk.Count -eq 1 -and -not (Test-ContainsOrdinal -Values @($Baseline.allowedMetadata.risks) -Candidate $risk[0])) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unsupported Risk '$($risk[0])'."
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Capability')) {
        if (-not (Test-ContainsOrdinal -Values $allowedCapabilities -Candidate $value)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Capability '$value'."
        }
    }
    foreach ($value in @(Get-TraitValues -Traits $Test.traits -Name 'Owner')) {
        if (-not (Test-ContainsOrdinal -Values $allowedOwners -Candidate $value)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CLASSIFICATION" -Message "$Location has unregistered Owner '$value'."
        }
    }
}

function Test-GovernedTestMetadataAndRoute {
    param(
        [Parameter(Mandatory)][object]$Test,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory)][string]$Location
    )

    Test-NewTestMetadata -Test $Test -Baseline $Baseline -Errors $Errors -Location $Location
    $testKind = @(Get-TraitValues -Traits $Test.traits -Name 'TestKind')
    $runtime = @(Get-TraitValues -Traits $Test.traits -Name 'Runtime')

    if (@($Project.allowedNewTestKinds).Count -gt 0 -and
        ($testKind.Count -ne 1 -or -not (Test-ContainsOrdinal -Values @($Project.allowedNewTestKinds) -Candidate $testKind[0]))) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$Location must use one of [$(@($Project.allowedNewTestKinds) -join ', ')]."
    }
    foreach ($runtimeValue in $runtime) {
        if (@($Project.allowedNewRuntimes).Count -gt 0 -and
            -not (Test-ContainsOrdinal -Values @($Project.allowedNewRuntimes) -Candidate $runtimeValue)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$Location Runtime '$runtimeValue' is not allowed in $($Project.projectName)."
        }
    }
    if ($testKind.Count -eq 1 -and (Test-ContainsOrdinal -Values @($Project.forbiddenNewTestKinds) -Candidate $testKind[0])) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-ROUTE" -Message "$Location must leave $($Project.projectName); TestKind '$($testKind[0])' is forbidden here."
    }
}

function Test-IsFrozenTest {
    param(
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][object]$Test
    )

    if ([string]$Project.freezeMode -in @('All', 'Structure')) { return $true }
    if ([string]$Project.freezeMode -ne 'Types') { return $false }
    foreach ($pattern in @($Project.frozenTypePatterns)) {
        if ([string]$Test.executionType -like $pattern -or [string]$Test.declaringType -like $pattern) {
            return $true
        }
    }
    return $false
}

function Get-ProjectedCasesPerExecution {
    param([Parameter(Mandatory)][object]$Test)

    $executionCount = @($Test.executionTypes).Count
    if ($executionCount -le 0) { return 0 }
    return [int]([int]$Test.projectedCases / $executionCount)
}

function Get-RemovedInlineDataSignatureOccurrences {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$BaselineSignatures,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$CurrentSignatures
    )

    $baselineCounts = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
    $currentCounts = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
    foreach ($signature in $BaselineSignatures) {
        if (-not $baselineCounts.ContainsKey($signature)) { $baselineCounts[$signature] = 0 }
        $baselineCounts[$signature]++
    }
    foreach ($signature in $CurrentSignatures) {
        if (-not $currentCounts.ContainsKey($signature)) { $currentCounts[$signature] = 0 }
        $currentCounts[$signature]++
    }
    $removed = [System.Collections.Generic.List[object]]::new()
    foreach ($signature in @($baselineCounts.Keys | Sort-Object)) {
        $currentCount = if ($currentCounts.ContainsKey($signature)) { $currentCounts[$signature] } else { 0 }
        $removedCount = $baselineCounts[$signature] - $currentCount
        for ($occurrence = 1; $occurrence -le $removedCount; $occurrence++) {
            $removed.Add([pscustomobject]@{ Signature = $signature; Occurrence = $occurrence })
        }
    }
    return [object[]]$removed
}

function Get-ProjectedCaseDecreaseDeltaTest {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $baselinePerExecution = Get-ProjectedCasesPerExecution -Test $BaselineTest
    $currentPerExecution = Get-ProjectedCasesPerExecution -Test $CurrentTest
    if ($currentPerExecution -ge $baselinePerExecution) { return $null }

    $removedInlineDataSignatures = @(Get-RemovedInlineDataSignatureOccurrences `
        -BaselineSignatures @($BaselineTest.inlineDataSignatures) `
        -CurrentSignatures @($CurrentTest.inlineDataSignatures))
    if ($removedInlineDataSignatures.Count -gt 0) { return $null }
    $identityMaterial = @(
        [string]$BaselineTest.id,
        [string]$baselinePerExecution,
        [string]$currentPerExecution,
        (@($removedInlineDataSignatures | ForEach-Object { "$($_.Signature)#$($_.Occurrence)" }) -join '|')
    ) -join '|'
    $deltaTest = $BaselineTest.PSObject.Copy()
    $deltaTest.id = "cloud-test-case-decrease-v1:$(ConvertTo-Sha256 $identityMaterial)"
    $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
    $deltaTest | Add-Member -NotePropertyName baselineCasesPerExecution -NotePropertyValue $baselinePerExecution -Force
    $deltaTest | Add-Member -NotePropertyName currentCasesPerExecution -NotePropertyValue $currentPerExecution -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLostPerExecution -NotePropertyValue ($baselinePerExecution - $currentPerExecution) -Force
    $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (($baselinePerExecution - $currentPerExecution) * @($CurrentTest.executionTypes).Count) -Force
    $deltaTest | Add-Member -NotePropertyName removedInlineDataSignatures -NotePropertyValue ([string[]]@($removedInlineDataSignatures | ForEach-Object { $_.Signature })) -Force
    return $deltaTest
}

function Get-InlineDataRemovalDeltaTests {
    param(
        [Parameter(Mandatory)][object]$BaselineTest,
        [Parameter(Mandatory)][object]$CurrentTest
    )

    $executionCount = @($CurrentTest.executionTypes).Count
    foreach ($removed in @(Get-RemovedInlineDataSignatureOccurrences `
        -BaselineSignatures @($BaselineTest.inlineDataSignatures) `
        -CurrentSignatures @($CurrentTest.inlineDataSignatures))) {
        $signature = [string]$removed.Signature
        $occurrence = [int]$removed.Occurrence
        $deltaTest = $BaselineTest.PSObject.Copy()
        $deltaTest.id = "cloud-test-inline-removal-v1:$(ConvertTo-Sha256 "$($BaselineTest.id)|$signature|$occurrence")"
        $deltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$BaselineTest.id) -Force
        $deltaTest | Add-Member -NotePropertyName removedInlineDataSignature -NotePropertyValue ([string]$signature) -Force
        $deltaTest | Add-Member -NotePropertyName removedInlineDataOccurrence -NotePropertyValue $occurrence -Force
        $deltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue $executionCount -Force
        Write-Output $deltaTest
    }
}

function Test-WaiverManifest {
    param(
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    if ([string]$WaiverManifest.schemaVersion -ne $waiverSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "unsupported waiver schemaVersion '$($WaiverManifest.schemaVersion)'."
    }
    if ([string]$WaiverManifest.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver manifest ruleId must be $ruleId."
    }

    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $seenRegressionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $today = [DateOnly]::FromDateTime([DateTime]::UtcNow)
    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath })
    foreach ($waiver in @($WaiverManifest.waivers)) {
        $required = @('id', 'projectPath', 'symbol', 'changeKind', 'regressionId', 'targetProject', 'testKind', 'owner', 'reason', 'approvedBy', 'expiresOn')
        foreach ($name in $required) {
            if ([string]::IsNullOrWhiteSpace([string](Get-OptionalProperty $waiver $name ''))) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver is missing '$name'."
            }
        }
        $id = [string](Get-OptionalProperty $waiver 'id' '')
        if (-not [string]::IsNullOrWhiteSpace($id) -and -not $seenIds.Add($id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "duplicate waiver id '$id'."
        }
        $regressionId = [string](Get-OptionalProperty $waiver 'regressionId' '')
        if (-not [string]::IsNullOrWhiteSpace($regressionId) -and -not $seenRegressionIds.Add($regressionId)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "regressionId '$regressionId' is claimed by more than one waiver."
        }
        foreach ($name in @('projectPath', 'symbol', 'regressionId', 'targetProject')) {
            $value = [string](Get-OptionalProperty $waiver $name '')
            if ($value -match '[*?\[\]]') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' must not use wildcard $name '$value'."
            }
        }
        $targetProject = [string](Get-OptionalProperty $waiver 'targetProject' '')
        $projectPathValue = [string](Get-OptionalProperty $waiver 'projectPath' '')
        if ($projectPathValue -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' source project '$projectPathValue' is not a reviewed test project."
        }
        if ($targetProject -eq $projectPathValue) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject must leave the frozen legacy bucket."
        }
        if ($targetProject -notin $baselineProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' targetProject '$targetProject' is not a reviewed test project."
        }
        $changeKind = [string](Get-OptionalProperty $waiver 'changeKind' '')
        if ($changeKind -notin @('Add', 'AttributeChange', 'InlineDataIncrease', 'InlineDataChange', 'InlineDataRemoval', 'DynamicDataSourceChange', 'ExecutionTypeIncrease', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'Remove')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unsupported changeKind '$changeKind'."
        }
        $testKind = [string](Get-OptionalProperty $waiver 'testKind' '')
        $owner = [string](Get-OptionalProperty $waiver 'owner' '')
        $approvedBy = [string](Get-OptionalProperty $waiver 'approvedBy' '')
        if (-not (Test-ContainsOrdinal -Values $allowedTestKinds -Candidate $testKind)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered testKind '$testKind'."
        }
        if (-not (Test-ContainsOrdinal -Values $allowedOwners -Candidate $owner)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' has unregistered owner '$owner'."
        }
        if (-not (Test-ContainsOrdinal -Values $approvedWaiverApprovers -Candidate $approvedBy)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' approver '$approvedBy' is not registered."
        }
        $expiresOnValue = [string](Get-OptionalProperty $waiver 'expiresOn' '')
        try {
            $expiresOn = [DateOnly]::ParseExact($expiresOnValue, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            if ($expiresOn -lt $today) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expired on $expiresOnValue."
            } elseif ($expiresOn.DayNumber - $today.DayNumber -gt $maximumWaiverDays) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' exceeds the $maximumWaiverDays-day maximum."
            }
        } catch {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$id' expiresOn '$expiresOnValue' is not yyyy-MM-dd."
        }
    }
}

function Test-BaselineStructure {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors,
        [switch]$AllowSyntheticPolicy
    )

    if ([string]$Baseline.schemaVersion -ne $baselineSchemaVersion) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unsupported baseline schemaVersion '$($Baseline.schemaVersion)'."
    }
    if ([string]$Baseline.ruleId -ne $ruleId) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "baseline ruleId must be $ruleId."
    }
    if (-not $AllowSyntheticPolicy) {
        if ([string](Get-OptionalProperty $Baseline.provenance 'sourceHead' '') -ne $reviewedBaselineSourceHead -or
            [string](Get-OptionalProperty $Baseline.provenance 'baselineStatus' '') -ne 'ReviewedSource') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "baseline provenance must remain anchored to reviewed source $reviewedBaselineSourceHead with status ReviewedSource."
        }
        if ([string](Get-OptionalProperty $Baseline 'attributeSignatureSchema' '') -ne 'cloud-cad-v1') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'attributeSignatureSchema must be cloud-cad-v1.'
        }
        if ([string](Get-OptionalProperty $Baseline.scanner 'engine' '') -ne 'System.Reflection.MetadataLoadContext' -or
            [string](Get-OptionalProperty $Baseline.scanner 'activeDotnetSdk' '') -ne $approvedDotnetSdk) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "baseline scanner must use the reviewed $approvedDotnetSdk MetadataLoadContext contract."
        }
        $baselineScannerHashes = Get-OptionalProperty $Baseline.scanner 'metadataLoadContextSha256ByPlatform' $null
        $expectedScannerPlatforms = @(Get-OrdinalSortedUniqueStrings -Values @($approvedMetadataLoadContextSha256ByPlatform.Keys))
        $actualScannerPlatforms = if ($null -eq $baselineScannerHashes) { @() } else {
            @(Get-OrdinalSortedUniqueStrings -Values @($baselineScannerHashes.PSObject.Properties | ForEach-Object { [string]$_.Name }))
        }
        if (($actualScannerPlatforms -join '|') -cne ($expectedScannerPlatforms -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message 'baseline scanner platform/hash roster differs from the reviewed MetadataLoadContext map.'
        }
        else {
            foreach ($platform in $expectedScannerPlatforms) {
                $actualScannerHash = [string]$baselineScannerHashes.PSObject.Properties[$platform].Value
                $expectedScannerHash = [string]$approvedMetadataLoadContextSha256ByPlatform[$platform]
                if ($actualScannerHash -cne $expectedScannerHash) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "baseline scanner hash for $platform differs from the reviewed MetadataLoadContext binary."
                }
            }
        }
        $canonicalProtectedPaths = @(Get-CanonicalProtectedAssetPaths | Sort-Object -Unique)
        $protectedAssets = @((Get-OptionalProperty $Baseline 'protectedAssets' @()))
        $protectedPaths = @($protectedAssets | ForEach-Object { [string]$_.path } | Sort-Object -Unique)
        if (($protectedPaths -join '|') -cne ($canonicalProtectedPaths -join '|') -or $protectedAssets.Count -ne $canonicalProtectedPaths.Count) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'protectedAssets roster differs from the canonical Cloud governance/build/test trust graph.'
        }
        foreach ($asset in $protectedAssets) {
            if ([string]$asset.sha256 -notmatch '^[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "protected asset '$($asset.path)' has an invalid SHA-256."
            }
        }
        foreach ($manifestName in @('workflowManifest', 'projectManifest', 'buildControlManifest', 'restoreControlManifest', 'frontendTestManifest')) {
            $manifest = Get-OptionalProperty $Baseline $manifestName $null
            if ($null -eq $manifest -or [int](Get-OptionalProperty $manifest 'count' -1) -lt 1 -or
                [string](Get-OptionalProperty $manifest 'sha256' '') -notmatch '^[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$manifestName must contain a positive count and exact SHA-256."
            }
        }
        if ([int](Get-OptionalProperty $Baseline.frontendTestManifest 'runnerCases' -1) -ne 67 -or
            [int](Get-OptionalProperty $Baseline.deploymentBehavior 'runnerCases' -1) -ne 33 -or
            [string](Get-OptionalProperty $Baseline.deploymentBehavior 'sourceSha256' '') -notmatch '^[0-9a-f]{64}$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'frontend/deployment execution baselines must remain exactly 67 and 33 with a protected deployment source.'
        }
    }
    foreach ($metadata in @(
        @{ Name = 'testKinds'; Expected = $allowedTestKinds },
        @{ Name = 'runtimes'; Expected = $allowedRuntimes },
        @{ Name = 'risks'; Expected = $allowedRisks },
        @{ Name = 'owners'; Expected = $allowedOwners },
        @{ Name = 'capabilities'; Expected = $allowedCapabilities }
    )) {
        $actual = @(Get-OrdinalSortedUniqueStrings -Values @((Get-OptionalProperty $Baseline.allowedMetadata $metadata.Name @())))
        $expected = @(Get-OrdinalSortedUniqueStrings -Values @($metadata.Expected))
        if (($actual -join '|') -cne ($expected -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "allowedMetadata.$($metadata.Name) differs from the canonical registry."
        }
    }
    $seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($project in @($Baseline.projects)) {
        if (-not $seenProjects.Add([string]$project.projectPath)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate project '$($project.projectPath)'."
        }
        if (-not $AllowSyntheticPolicy) {
            $expectedPolicy = Get-GeneratedProjectPolicy -GeneratedProjectName ([string]$project.projectName)
            foreach ($name in @('freezeMode', 'protectBaselineRemovals')) {
                if ([string](Get-OptionalProperty $project $name '') -ne [string](Get-OptionalProperty $expectedPolicy $name '')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            foreach ($name in @('frozenTypePatterns', 'allowedNewTestKinds', 'allowedNewRuntimes', 'forbiddenNewTestKinds')) {
                $actual = @(Get-OrdinalSortedUniqueStrings -Values @((Get-OptionalProperty $project $name @())))
                $expected = @(Get-OrdinalSortedUniqueStrings -Values @((Get-OptionalProperty $expectedPolicy $name @())))
                if (($actual -join '|') -cne ($expected -join '|')) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) $name differs from the canonical project policy."
                }
            }
            $actualCeilings = (Get-OptionalProperty $project 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            $expectedCeilings = (Get-OptionalProperty $expectedPolicy 'discoveryCeilings' @()) | ConvertTo-Json -Depth 20 -Compress
            if ($actualCeilings -ne $expectedCeilings) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) discoveryCeilings differs from the canonical project policy."
            }
            $expectedHashPaths = if ([string]$project.freezeMode -eq 'All') {
                @($project.frozenSourceFiles | ForEach-Object { [string]$_ } | Sort-Object -Unique)
            } else { @() }
            $actualHashPaths = @((Get-OptionalProperty $project 'frozenSourceHashes' ([pscustomobject]@{})).PSObject.Properties |
                ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
            if (($actualHashPaths -join '|') -cne ($expectedHashPaths -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) frozenSourceHashes must exactly cover its frozen source roster."
            }
        }
        $seenTests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $executionTemplateCount = 0
        $projectedCaseCount = 0
        foreach ($test in @($project.tests)) {
            if (-not $seenTests.Add([string]$test.id)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "duplicate test id '$($test.id)' in $($project.projectPath)."
            }
            if ([string]$test.id -notmatch '^cloud-test-physical-v1:[0-9a-f]{64}$' -or [string]$test.logicalId -notmatch '^cloud-test-decl-v1:[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid stable identity for '$($test.symbol)' in $($project.projectPath)."
            }
            if (-not (Test-ContainsOrdinal -Values $allowedTestAttributeTypes -Candidate ([string]$test.testAttributeType))) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "unregistered test attribute '$($test.testAttributeType)' for '$($test.symbol)'."
            }
            if (@($test.executionTypes).Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "test declaration '$($test.symbol)' has no concrete execution type."
            }
            $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($executionType in @($test.executionTypes)) {
                if ([string]$executionType.id -notmatch '^cloud-test-execution-v1:[0-9a-f]{64}$' -or -not $executionIds.Add([string]$executionType.id)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "invalid or duplicate execution identity for '$($test.symbol)'."
                }
                $executionTemplateCount++
            }
            if ([int]$test.projectedCases % @($test.executionTypes).Count -ne 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "projected case count for '$($test.symbol)' is not divisible by its concrete execution count."
            }
            $inlineSignatures = @((Get-OptionalProperty $test 'inlineDataSignatures' @()))
            if ([int]$test.inlineDataRows -ne $inlineSignatures.Count -or
                @($inlineSignatures | Where-Object { [string]$_ -notmatch '^cloud-cad-v1:[0-9a-f]{64}$' }).Count -gt 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "InlineData signatures for '$($test.symbol)' do not match the canonical typed payload schema/count."
            }
            $expectedProjectedCases = if ([string]$test.attributeCategory -eq 'Theory' -and [int]$test.inlineDataRows -gt 0) {
                [int]$test.inlineDataRows * @($test.executionTypes).Count
            } else { @($test.executionTypes).Count }
            if ([int]$test.projectedCases -ne $expectedProjectedCases) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "projected case count for '$($test.symbol)' does not match its attribute/data/execution shape."
            }
            $projectedCaseCount += [int]$test.projectedCases
        }
        if ([int](Get-OptionalProperty $project 'baselineDeclarations' -1) -ne @($project.tests).Count -or
            [int](Get-OptionalProperty $project 'baselineExecutionTemplates' -1) -ne $executionTemplateCount -or
            [int](Get-OptionalProperty $project 'baselineProjectedCases' -1) -ne $projectedCaseCount) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($project.projectName) summary counts do not match its immutable test records."
        }
        if (-not $AllowSyntheticPolicy) {
            $baselineRunnerCaseValues = @((Get-OptionalProperty $project 'runnerCases' @()) | ForEach-Object { [string]$_ })
            $baselineRunnerDigest = ConvertTo-Sha256 -Value ($baselineRunnerCaseValues -join "`n")
            if ([int](Get-OptionalProperty $project 'baselineRunnerCases' -1) -ne $projectedCaseCount -or
                [int](Get-OptionalProperty $project 'baselineRunnerCases' -1) -ne $baselineRunnerCaseValues.Count -or
                [string](Get-OptionalProperty $project 'runnerCaseDigest' '') -cne $baselineRunnerDigest -or
                @($baselineRunnerCaseValues | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) must preserve an internally consistent exact Release runner roster/digest equal to its static projection."
            }
        }
    }

    if (-not $AllowSyntheticPolicy) {
        $baselineProjectPaths = @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)
        $expectedWorkflowPaths = @('.github/workflows/cloud-ci.yml')
        $actualWorkflowPaths = @($Baseline.ciRequirements | ForEach-Object { [string]$_.workflowPath } | Sort-Object -Unique)
        if (($actualWorkflowPaths -join '|') -cne ($expectedWorkflowPaths -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message 'ciRequirements must protect the canonical Cloud workflow.'
        }
        foreach ($requirement in @($Baseline.ciRequirements)) {
            if ([string](Get-OptionalProperty $requirement 'jobSha256' '') -notmatch '^[0-9a-f]{64}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) must preserve an exact required job SHA-256."
            }
            $requiredProjects = @($requirement.requiredTestProjects | ForEach-Object { [string]$_ } | Sort-Object -Unique)
            if (($requiredProjects -join '|') -cne ($baselineProjectPaths -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredTestProjects differs from the reviewed project set."
            }
            $requiredCommands = @($requirement.requiredCommandPrefixes | ForEach-Object { [string]$_ })
            $canonicalCommands = @(Get-CanonicalRequiredCommandPrefixes)
            if (($requiredCommands -join '|') -cne ($canonicalCommands -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "$($requirement.workflowPath) requiredCommandPrefixes differs from the canonical gate order."
            }
        }
    }
}

function Test-StaticPolicy {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    Test-BaselineStructure -Baseline $Baseline -Errors $Errors
    Test-WaiverManifest -WaiverManifest $WaiverManifest -Baseline $Baseline -Errors $Errors
    Test-ScannerContract -Errors $Errors

    $repositoryFiles = @(Get-RepositoryFiles -Root $RepositoryRoot)
    $caseInsensitivePaths = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $repositoryFiles) {
        $relativePath = [string]$file.RelativePath
        if ($relativePath -cne $relativePath.Normalize([Text.NormalizationForm]::FormC) -or
            $relativePath -match '[\x00-\x1f\x7f\\]' -or
            [IO.Path]::IsPathRooted($relativePath) -or
            @($relativePath.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "repository path is not canonical NFC relative form: '$relativePath'."
        }
        if ($caseInsensitivePaths.ContainsKey($relativePath) -and $caseInsensitivePaths[$relativePath] -cne $relativePath) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "repository paths collide on case-insensitive filesystems: '$($caseInsensitivePaths[$relativePath])' and '$relativePath'."
        } else {
            $caseInsensitivePaths[$relativePath] = $relativePath
        }
        $fileItem = Get-Item -LiteralPath $file.FullName -Force
        if (($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "governed repository file must not be a symlink/reparse point: '$relativePath'."
        }
    }

    $protectedAssets = @((Get-OptionalProperty $Baseline 'protectedAssets' @()))
    foreach ($asset in $protectedAssets) {
        $assetPath = Join-Path $RepositoryRoot ([string]$asset.path)
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$asset.sha256) {
            $assetCode = switch -Regex ([string]$asset.path) {
                '^\.github/CODEOWNERS$' { "$ruleId-CODEOWNER"; break }
                '^\.github/workflows/' { "$ruleId-CI"; break }
                '^\.gitattributes$' { "$ruleId-CONFIG"; break }
                default { "$ruleId-BASELINE" }
            }
            Add-PolicyError -Errors $Errors -Code $assetCode -Message "protected trust asset changed without a reviewed baseline transition: '$($asset.path)'."
        }
        if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($assetPath)
            if ([Array]::IndexOf($bytes, [byte]13) -ge 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "protected text asset must use LF line endings: '$($asset.path)'."
            }
        }
    }

    $workflowManifestEntries = @(Get-WorkflowManifestEntries -Root $RepositoryRoot)
    if ($workflowManifestEntries.Count -ne [int]$Baseline.workflowManifest.count -or
        (Get-ManifestDigest -Entries $workflowManifestEntries) -ne [string]$Baseline.workflowManifest.sha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message 'workflow roster/content differs from the exact reviewed Cloud workflow graph.'
    }
    $buildControlEntries = @(Get-BuildControlManifestEntries -Root $RepositoryRoot)
    if ($buildControlEntries.Count -ne [int]$Baseline.buildControlManifest.count -or
        (Get-ManifestDigest -Entries $buildControlEntries) -ne [string]$Baseline.buildControlManifest.sha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'reviewed MSBuild/Roslyn/runtime control roster or content differs (Directory.Build/Solution hooks, project user/extensions, .editorconfig/.globalconfig, runtimeconfig.template.json).'
    }
    $rootBuildPropsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $rootBuildPropsPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DEPENDENCY" -Message 'Directory.Build.props is required to enforce fail-closed NuGet audit.'
    } else {
        try {
            [xml]$rootBuildPropsXml = Get-Content -LiteralPath $rootBuildPropsPath -Raw
            foreach ($auditProperty in @(
                @{ Name = 'NuGetAudit'; Value = 'true' },
                @{ Name = 'NuGetAuditMode'; Value = 'all' },
                @{ Name = 'NuGetAuditLevel'; Value = 'low' }
            )) {
                $nodes = @($rootBuildPropsXml.SelectNodes("/Project/PropertyGroup/$($auditProperty.Name)"))
                if ($nodes.Count -ne 1 -or [string]$nodes[0].InnerText -cne [string]$auditProperty.Value) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-DEPENDENCY" -Message "Directory.Build.props must set exactly one $($auditProperty.Name)=$($auditProperty.Value)."
                }
            }
            $warningsAsErrorsNodes = @($rootBuildPropsXml.SelectNodes('/Project/PropertyGroup/WarningsAsErrors'))
            $warningsAsErrors = if ($warningsAsErrorsNodes.Count -eq 1) {
                @([string]$warningsAsErrorsNodes[0].InnerText -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            } else { @() }
            foreach ($auditCode in @('NU1900', 'NU1901', 'NU1902', 'NU1903', 'NU1904', 'NU1905')) {
                if (-not (Test-ContainsOrdinal -Values $warningsAsErrors -Candidate $auditCode)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-DEPENDENCY" -Message "Directory.Build.props must promote $auditCode to an error."
                }
            }
        } catch {
            Add-PolicyError -Errors $Errors -Code "$ruleId-DEPENDENCY" -Message "Directory.Build.props NuGet audit contract cannot be parsed: $($_.Exception.GetType().Name)."
        }
    }
    $projectManifestEntries = @(Get-ProjectManifestEntries -Root $RepositoryRoot)
    if ($projectManifestEntries.Count -ne [int]$Baseline.projectManifest.count -or
        (Get-ManifestDigest -Entries $projectManifestEntries) -ne [string]$Baseline.projectManifest.sha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message 'csproj roster/content differs from the exact reviewed restore/build graph.'
    }
    $restoreControlEntries = @(Get-RestoreControlManifestEntries -Root $RepositoryRoot)
    if ($restoreControlEntries.Count -ne [int]$Baseline.restoreControlManifest.count -or
        (Get-ManifestDigest -Entries $restoreControlEntries) -ne [string]$Baseline.restoreControlManifest.sha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'NuGet/npm restore-control roster or content differs from the exact reviewed graph.'
    }
    $frontendTestEntries = @(Get-FrontendUnitTestManifestEntries -Root $RepositoryRoot)
    if ($frontendTestEntries.Count -ne [int]$Baseline.frontendTestManifest.count -or
        (Get-ManifestDigest -Entries $frontendTestEntries) -ne [string]$Baseline.frontendTestManifest.sha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message 'frontend unit-test roster/content differs from the reviewed 67-case Phase 0 baseline.'
    }
    $deploymentBehaviorPath = Join-Path $RepositoryRoot 'deploy/tests/deployment-behavior.sh'
    if ((Get-FileHash -LiteralPath $deploymentBehaviorPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$Baseline.deploymentBehavior.sourceSha256) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message 'deployment behavior source differs from the reviewed 33-scenario Phase 0 baseline.'
    }
    foreach ($responseFile in @($repositoryFiles | Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.rsp' })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-RESPONSE" -Message "repository response file can alter restore/build/test before governance: $($responseFile.RelativePath)."
    }

    $baselineProjects = @($Baseline.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object -Unique)
    $currentProjects = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration -AllowMissingAssembly |
        ForEach-Object { Get-RelativePath -BasePath $RepositoryRoot -Path $_.ProjectPath } |
        Sort-Object -Unique)
    if (($baselineProjects -join '|') -ne ($currentProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "test project set differs from the reviewed baseline. Current=[$($currentProjects -join ', ')], baseline=[$($baselineProjects -join ', ')]."
    }

    $solutionPath = Join-Path $RepositoryRoot 'IIoT.CloudPlatform.slnx'
    [xml]$solutionXml = Get-Content $solutionPath -Raw
    $solutionProjects = @($solutionXml.SelectNodes('//Project') | ForEach-Object { [string]$_.Path })
    foreach ($projectPathValue in $currentProjects) {
        if ($projectPathValue -notin $solutionProjects) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$projectPathValue is not included in IIoT.CloudPlatform.slnx."
        }
    }

    $nestedTargets = @($repositoryFiles | Where-Object {
        $_.RelativePath.StartsWith('src/tests/', [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($_.RelativePath) -ieq 'Directory.Build.targets'
    })
    foreach ($nestedTarget in $nestedTargets) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "nested targets file shadows the root hard gate: $($nestedTarget.RelativePath)."
    }
    $testRoot = Get-NormalizedPath (Join-Path $RepositoryRoot 'src/tests')
    $canonicalTestBuildProps = Get-NormalizedPath (Join-Path $testRoot 'Directory.Build.props')
    $rootBuildTargets = Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')
    if (-not (Test-Path $rootBuildTargets -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message 'Directory.Build.targets is missing from the reviewed Cloud hard-gate wiring.'
    }
    if (-not (Test-Path $canonicalTestBuildProps -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests/Directory.Build.props is missing from the reviewed shared runner/analyzer configuration.'
    }
    $codeOwnersPath = Join-Path $RepositoryRoot '.github/CODEOWNERS'
    if (-not (Test-Path $codeOwnersPath -PathType Leaf)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message '.github/CODEOWNERS is missing; governance assets and test method bodies require reviewed ownership.'
    } else {
        $codeOwnersContent = (Get-Content $codeOwnersPath -Raw).Replace("`r`n", "`n")
        foreach ($requiredCodeOwnerRule in @(
            '/.github/workflows/** @ShuJinHao',
            '/.gitattributes @ShuJinHao',
            '/global.json @ShuJinHao',
            '/Directory.Build.props @ShuJinHao',
            '/Directory.Build.targets @ShuJinHao',
            '**/Directory.Solution.props @ShuJinHao',
            '**/Directory.Solution.targets @ShuJinHao',
            '**/before.*.sln.targets @ShuJinHao',
            '**/after.*.sln.targets @ShuJinHao',
            '**/*.*proj.user @ShuJinHao',
            '**/obj/*.*proj.*.props @ShuJinHao',
            '**/obj/*.*proj.*.targets @ShuJinHao',
            '**/.editorconfig @ShuJinHao',
            '**/.globalconfig @ShuJinHao',
            '**/runtimeconfig.template.json @ShuJinHao',
            '/scripts/tests/CloudTestAttributeCodec.psm1 @ShuJinHao',
            '/scripts/tests/TestCloudTestGovernancePolicy.ps1 @ShuJinHao',
            '/scripts/tests/TestCloudTestGovernanceBehavior.ps1 @ShuJinHao',
            '/scripts/tests/baselines/ @ShuJinHao',
            '/src/tests/**/*.cs @ShuJinHao',
            '/src/tests/**/*.csproj @ShuJinHao',
            '/src/tests/Directory.Build.props @ShuJinHao',
            '/src/tests/xunit.runner.json @ShuJinHao'
        )) {
            if ($codeOwnersContent -notmatch "(?m)^$([regex]::Escape($requiredCodeOwnerRule))$") {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CODEOWNER" -Message "CODEOWNERS does not exactly protect '$requiredCodeOwnerRule'."
            }
        }
    }
    $testSourceFiles = @($repositoryFiles | Where-Object {
        $_.FullName.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and
        [IO.Path]::GetExtension($_.RelativePath) -ieq '.cs'
    })
    $testSourceText = (@($testSourceFiles | ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw).Replace("`r`n", "`n") }) -join "`n")
    $workspaceAlignmentAttribute = @'
public sealed class WorkspaceAlignmentFactAttribute : FactAttribute
{
    public WorkspaceAlignmentFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("IIOT_RUN_CLOUD_AI_WORKSPACE_ALIGNMENT"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set IIOT_RUN_CLOUD_AI_WORKSPACE_ALIGNMENT=1 to run the real Cloud/AICopilot workspace alignment.";
        }
    }
}
'@
    $workspaceDefinitionCount = [regex]::Matches($testSourceText, '(?m)^public sealed class WorkspaceAlignmentFactAttribute\s*:\s*FactAttribute\s*$').Count
    $workspaceUsageCount = [regex]::Matches($testSourceText, '(?m)^\s*\[WorkspaceAlignmentFact\]\s*$').Count
    if ($workspaceDefinitionCount -ne 1 -or $workspaceUsageCount -ne 1 -or -not $testSourceText.Contains($workspaceAlignmentAttribute.Trim(), [StringComparison]::Ordinal)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-WORKSPACE-ALIGNMENT" -Message 'the single reviewed WorkspaceAlignmentFact definition/usage or its fail-closed environment contract changed.'
    }
    $sourceWithoutWorkspaceException = $testSourceText.Replace($workspaceAlignmentAttribute.Trim(), '')
    if ($sourceWithoutWorkspaceException -match '(?m):\s*(?:Fact|Theory)Attribute\s*$') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'custom Fact/Theory attributes are forbidden; WorkspaceAlignmentFact is the only exact reviewed LiveExternal exception.'
    }
    if ($sourceWithoutWorkspaceException -match '(?m)^\s*(?:global\s+)?using\s+[^\s=;]+\s*=\s*(?:global::)?Xunit\s*\.\s*Assert\s*;') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'test source must not alias Xunit.Assert; arbitrary aliases can hide runtime Assert.Skip calls from the Phase 0 lexical guard.'
    }
    if ($sourceWithoutWorkspaceException -match '(?i)(?:\b(?:Assert|[A-Za-z_]\w*Assert\w*)\s*\.\s*Skip(?:When|Unless|If)?\s*\(|(?<![.\w])Skip(?:When|Unless|If)?\s*\(|\bSkip\s*=|\bSkipWhen\s*=|\bSkipUnless\s*=|\bExplicit\s*=)') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'test source contains a lexically visible static or runtime Skip/Explicit path outside the single reviewed LiveExternal exception.'
    }
    if ($testSourceText -match '(?m)^\s*\[(?:MemberData|ClassData)(?:Attribute)?\b') {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DISABLED" -Message 'Phase 0 forbids MemberData/ClassData because deterministic source-to-runner expansion is not yet governed.'
    }
    $unsupportedTestProjects = @($repositoryFiles | Where-Object {
        $_.FullName.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($_.RelativePath) -match '(?i)\.[A-Za-z0-9]+proj$' -and
        -not [string]::Equals([IO.Path]::GetExtension($_.RelativePath), '.csproj', [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($unsupportedTestProject in $unsupportedTestProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$($unsupportedTestProject.RelativePath) is a non-C# test project; Phase 0 permits only reviewed csproj projects."
    }
    $unsupportedRepositoryProjects = @($repositoryFiles | Where-Object {
        [IO.Path]::GetExtension($_.RelativePath) -in @('.fsproj', '.vbproj')
    })
    foreach ($unsupportedRepositoryProject in $unsupportedRepositoryProjects) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$($unsupportedRepositoryProject.RelativePath) uses an unreviewed project language that could hide a test project."
    }
    foreach ($runSettingsFile in @($repositoryFiles | Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.runsettings' })) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$($runSettingsFile.RelativePath) is an alternate VSTest configuration source; required lanes use only the canonical failSkips JSON."
    }
    $allTestRootProjects = @($repositoryFiles |
        Where-Object {
            $_.FullName.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and
            [IO.Path]::GetExtension($_.RelativePath) -ieq '.csproj'
        } |
        ForEach-Object { [string]$_.RelativePath } |
        Sort-Object -Unique)
    if (($allTestRootProjects -join '|') -ne ($baselineProjects -join '|')) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "every csproj under src/tests must be an explicitly reviewed baseline project. Found=[$($allTestRootProjects -join ', ')]."
    }
    foreach ($projectEntry in @($repositoryFiles | Where-Object { [IO.Path]::GetExtension($_.RelativePath) -ieq '.csproj' })) {
        $projectFile = Get-Item -LiteralPath $projectEntry.FullName -Force
        $projectContent = Get-Content -LiteralPath $projectFile.FullName -Raw
        [xml]$projectXml = $projectContent
        $relativeProjectPath = Get-RelativePath -BasePath $RepositoryRoot -Path $projectFile.FullName
        $normalizedProjectPath = Get-NormalizedPath $projectFile.FullName
        $isInsideTestRoot = $normalizedProjectPath.StartsWith("$testRoot/", [StringComparison]::Ordinal)
        if (@($projectXml.SelectNodes('//Import')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an explicit MSBuild import; project-local import indirection is not allowed in the reviewed graph."
        }
        $directIsTestProjectNodes = @($projectXml.SelectNodes('/Project/PropertyGroup/IsTestProject') | Where-Object { [string]$_.InnerText -eq 'true' })
        $allIsTestProjectNodes = @($projectXml.SelectNodes('//IsTestProject'))
        $conditionalDirectIsTestProjectNodes = @($directIsTestProjectNodes | Where-Object {
            $null -ne $_.Attributes['Condition'] -and -not [string]::IsNullOrWhiteSpace([string]$_.Attributes['Condition'].Value)
        })
        $testPackageNodes = @($projectXml.SelectNodes('//PackageReference') | Where-Object {
            [string]$_.Include -match '(?i)^(?:xunit(?:\.|$)|NUnit(?:\.|$)|MSTest(?:\.|$)|TUnit(?:\.|$)|Microsoft\.NET\.Test\.Sdk$|Microsoft\.Testing\.Platform(?:\.|$)|Microsoft\.TestPlatform(?:\.|$))'
        })
        $testingPlatformMarkers = @($projectXml.SelectNodes('//TestingPlatformApplication|//IsTestingPlatformApplication') | Where-Object { [string]$_.InnerText -match '(?i)^\s*true\s*$' })
        $indirectPackageIdentityNodes = @($projectXml.SelectNodes('//PackageReference') | Where-Object { [string]$_.Include -match '\$\(' })
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        if ([string]$projectXml.Project.Sdk -match '\$\(') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses an indirect Project SDK identity."
        }
        if ($projectContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath configures an alternate test-runner setting that can override failSkips."
        }
        if ($projectContent -match '(?i)<[A-Za-z0-9_.-]*(?:Xunit.*Runner|Runner.*Xunit|RunnerJson|TestRunner)[A-Za-z0-9_.-]*>') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath declares a project-local test runner property; only the shared reviewed configuration is allowed."
        }
        if ($conditionalDirectIsTestProjectNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath makes IsTestProject=true conditional; reviewed test identity must be direct and unconditional."
        }
        if (@($projectXml.SelectNodes('//DesignTimeBuild|//IsCrossTargetingBuild')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath defines an MSBuild lifecycle property that can suppress the required hard gate."
        }
        if (-not $isInsideTestRoot -and ($allIsTestProjectNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0 -or $projectFile.BaseName -match '(?i)(?:^|\.)Tests?$')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath declares or is named as a test project outside src/tests."
        }
        if (($testPackageNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0) -and $directIsTestProjectNodes.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath uses a test package without one explicit IsTestProject=true."
        }
        if (($testPackageNodes.Count -gt 0 -or $testingPlatformMarkers.Count -gt 0) -and -not $isInsideTestRoot) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath is a test project outside src/tests."
        }
        if ($directIsTestProjectNodes.Count -gt 0) {
            if (-not $isInsideTestRoot) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath explicitly declares IsTestProject=true outside src/tests."
            }
            if ($relativeProjectPath -notin $baselineProjects) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-PROJECT" -Message "$relativeProjectPath is an unreviewed test project outside the baseline/CI matrix."
            }
        }
        if ($relativeProjectPath -in $baselineProjects -and ($directIsTestProjectNodes.Count -ne 1 -or $allIsTestProjectNodes.Count -ne 1)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath must own exactly one unconditional, direct IsTestProject=true declaration."
        }
        $runnerConfigOverrides = @($projectXml.SelectNodes('//*[@Include or @Update or @Remove]') | Where-Object {
            @($_.Attributes | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeProjectPath overrides xunit.runner.json; only src/tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @($projectXml.SelectNodes('//Target') | Where-Object { [string]$_.Name -match '^ValidateCloudTestGovernance' })
        if ($gateTargetOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath overrides an Cloud test-governance MSBuild target."
        }
        if ($relativeProjectPath -in $baselineProjects) {
            $projectTargets = @($projectXml.SelectNodes('//Target'))
            $expectedTargetHash = [string]$allowedTestProjectTargetHashes[$relativeProjectPath]
            if ($projectTargets.Count -eq 0) {
                if (-not [string]::IsNullOrWhiteSpace($expectedTargetHash)) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath removed its reviewed MSBuild target set."
                }
            } elseif ([string]::IsNullOrWhiteSpace($expectedTargetHash)) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath introduces an unreviewed MSBuild target."
            } else {
                $targetMaterial = @($projectTargets | ForEach-Object { $_.OuterXml }) -join "`n"
                if ((ConvertTo-Sha256 -Value $targetMaterial) -ne $expectedTargetHash) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeProjectPath changed its reviewed MSBuild target set."
                }
            }
        }
        $disabledTargetImports = @($projectXml.SelectNodes('//ImportDirectoryBuildTargets') | Where-Object { [string]$_.InnerText -match '^\s*false\s*$' })
        $targetPathOverrides = @($projectXml.SelectNodes('//DirectoryBuildTargetsPath') | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.InnerText) })
        if ($disabledTargetImports.Count -gt 0 -or $targetPathOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $projectFile.FullName) disables or redirects the root Directory.Build.targets hard gate."
        }
    }
    foreach ($buildEntry in @($repositoryFiles | Where-Object { [IO.Path]::GetExtension($_.RelativePath) -in @('.props', '.targets') })) {
        $buildFile = Get-Item -LiteralPath $buildEntry.FullName -Force
        $buildContent = Get-Content -LiteralPath $buildFile.FullName -Raw
        [xml]$buildXml = $buildContent
        $normalizedBuildFile = Get-NormalizedPath $buildFile.FullName
        $relativeBuildFile = Get-RelativePath -BasePath $RepositoryRoot -Path $buildFile.FullName
        if ($normalizedBuildFile.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile is an unreviewed test props/targets indirection; only src/tests/Directory.Build.props is allowed."
        }
        $declaresTestProject = @($buildXml.SelectNodes('//IsTestProject') | Where-Object { [string]$_.InnerText -match '^\s*true\s*$' }).Count -gt 0
        $declaresTestPackage = @($buildXml.SelectNodes('//PackageReference') | Where-Object {
            [string]$_.Include -match '(?i)^(?:xunit(?:\.|$)|NUnit(?:\.|$)|MSTest(?:\.|$)|TUnit(?:\.|$)|Microsoft\.NET\.Test\.Sdk$|Microsoft\.Testing\.Platform(?:\.|$)|Microsoft\.TestPlatform(?:\.|$))'
        }).Count -gt 0
        $declaresTestingPlatform = @($buildXml.SelectNodes('//TestingPlatformApplication|//IsTestingPlatformApplication') | Where-Object {
            [string]$_.InnerText -match '(?i)^\s*true\s*$'
        }).Count -gt 0
        $indirectPackageIdentityNodes = @($buildXml.SelectNodes('//PackageReference') | Where-Object { [string]$_.Include -match '\$\(' })
        if ($indirectPackageIdentityNodes.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile uses an MSBuild expression as PackageReference identity and can hide test packages."
        }
        if ($buildContent -match '(?i)(?:RunSettings|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeBuildFile configures an alternate VSTest runsettings source that can override failSkips."
        }
        if (($declaresTestProject -or $declaresTestPackage -or $declaresTestingPlatform) -and -not (Get-NormalizedPath $buildFile.FullName).StartsWith("$testRoot/", [StringComparison]::Ordinal)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile defines imported test identity/packages outside src/tests."
        }
        if (@($buildXml.SelectNodes('//IsTestProject')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile must not define IsTestProject; every reviewed test csproj must own that identity directly."
        }
        if (@($buildXml.SelectNodes('//DesignTimeBuild|//IsCrossTargetingBuild')).Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile defines an MSBuild lifecycle property that can suppress the required hard gate."
        }
        $runnerConfigOverrides = @($buildXml.SelectNodes('//*[@Include or @Update or @Remove]') | Where-Object {
            @($_.Attributes | ForEach-Object { [string]$_.Value } | Where-Object { $_ -match '(?i)xunit\.runner\.json' }).Count -gt 0
        })
        if ($runnerConfigOverrides.Count -gt 0 -and $normalizedBuildFile -ne $canonicalTestBuildProps) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message "$relativeBuildFile overrides xunit.runner.json; only src/tests/Directory.Build.props may define it."
        }
        $gateTargetOverrides = @($buildXml.SelectNodes('//Target') | Where-Object { [string]$_.Name -match '^ValidateCloudTestGovernance' })
        $rootTargets = Get-NormalizedPath (Join-Path $RepositoryRoot 'Directory.Build.targets')
        if ($gateTargetOverrides.Count -gt 0 -and $normalizedBuildFile -ne $rootTargets) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$relativeBuildFile overrides an Cloud test-governance MSBuild target."
        }
        $disabledTargetImports = @($buildXml.SelectNodes('//ImportDirectoryBuildTargets') | Where-Object { [string]$_.InnerText -match '^\s*false\s*$' })
        $targetPathOverrides = @($buildXml.SelectNodes('//DirectoryBuildTargetsPath') | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.InnerText) })
        if ($disabledTargetImports.Count -gt 0 -or $targetPathOverrides.Count -gt 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $buildFile.FullName) disables or redirects the root Directory.Build.targets hard gate."
        }
    }

    foreach ($project in @($Baseline.projects | Where-Object { [string]$_.freezeMode -eq 'All' })) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $currentSources = @(Get-ProjectSourceFiles -ResolvedProjectPath $projectFile)
        if (($currentSources -join '|') -cne (@($project.frozenSourceFiles | Sort-Object -Unique) -join '|')) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$($project.projectName) source-file roster differs from its frozen Phase 0 baseline."
        }
        $sourceHashes = Get-OptionalProperty $project 'frozenSourceHashes' $null
        if ($null -ne $sourceHashes) {
            foreach ($hashProperty in @($sourceHashes.PSObject.Properties)) {
                $sourcePath = Join-Path $RepositoryRoot $hashProperty.Name
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
                    (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$hashProperty.Value) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$($hashProperty.Name) differs from the content-frozen EndToEnd test body/helper baseline."
                }
            }
        }
    }

    $canonicalCloudWorkflow = Get-NormalizedPath (Join-Path $RepositoryRoot '.github/workflows/cloud-ci.yml')
    foreach ($workflowEntry in @($repositoryFiles | Where-Object {
        $_.RelativePath.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -and
        [IO.Path]::GetExtension($_.RelativePath) -in @('.yml', '.yaml')
    })) {
        $workflowFile = Get-Item -LiteralPath $workflowEntry.FullName -Force
        if ((Get-NormalizedPath $workflowFile.FullName) -eq $canonicalCloudWorkflow) { continue }
        $otherWorkflowContent = (Get-Content $workflowFile.FullName -Raw).Replace("`r`n", "`n")
        if ($otherWorkflowContent -match '(?i)(?<![A-Za-z0-9_-])(?:cloud-ci|build-test)(?![A-Za-z0-9_-])') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$(Get-RelativePath -BasePath $RepositoryRoot -Path $workflowFile.FullName) duplicates the protected cloud-ci/build-test check identity."
        }
    }

    $runnerConfigs = @($repositoryFiles | Where-Object {
        $_.FullName.StartsWith("$testRoot/", [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($_.RelativePath) -match '(?i)xunit\.runner\.json$'
    } | ForEach-Object { Get-Item -LiteralPath $_.FullName -Force })
    $canonicalRunnerConfig = Join-Path $testRoot 'xunit.runner.json'
    if ($runnerConfigs.Count -ne 1 -or (Get-NormalizedPath $runnerConfigs[0].FullName) -ne (Get-NormalizedPath $canonicalRunnerConfig)) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests must contain exactly one shared xunit.runner.json; project-local overrides are forbidden.'
    } else {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $canonicalRunnerConfig -Errors $Errors -Context 'shared source configuration'
    }
    $testBuildProps = Join-Path $testRoot 'Directory.Build.props'
    [xml]$testBuildPropsXml = Get-Content $testBuildProps -Raw
    $rootBuildPropsImports = @($testBuildPropsXml.SelectNodes('/Project/Import') | Where-Object {
        [string]$_.Project -ceq '$(MSBuildThisFileDirectory)../../Directory.Build.props' -and
        $null -eq $_.Attributes['Condition']
    })
    if ($rootBuildPropsImports.Count -ne 1 -or @($testBuildPropsXml.SelectNodes('/Project/Import')).Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-DEPENDENCY" -Message 'src/tests/Directory.Build.props must unconditionally import the one reviewed root Directory.Build.props before test-specific settings.'
    }
    $runnerConfigItems = @($testBuildPropsXml.SelectNodes('//None') | Where-Object {
        [string]$_.Include -eq '$(MSBuildThisFileDirectory)xunit.runner.json' -and
        [string]$_.Link -eq 'xunit.runner.json' -and
        [string]$_.CopyToOutputDirectory -eq 'PreserveNewest'
    })
    if ($runnerConfigItems.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-CONFIG" -Message 'src/tests/Directory.Build.props must copy the single failSkips runner configuration into every test output.'
    }

    foreach ($requirement in @($Baseline.ciRequirements)) {
        $workflowPath = Join-Path $RepositoryRoot $requirement.workflowPath
        if (-not (Test-Path $workflowPath -PathType Leaf)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "required workflow does not exist: $($requirement.workflowPath)."
            continue
        }
        $workflowContent = (Get-Content $workflowPath -Raw).Replace('\', '/')
        Test-WorkflowDocumentSemantics -WorkflowContent $workflowContent -Requirement $requirement -Errors $Errors -Context ([string]$requirement.workflowPath)
        $requiredJobName = 'build-test'
        $jobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $workflowContent -JobName $requiredJobName)
        if ($jobEnvelopes.Count -ne 1 -or $jobEnvelopes[0].HasIf -or $jobEnvelopes[0].Ambiguous -or
            @($jobEnvelopes[0].UnexpectedKeys).Count -gt 0 -or
            @($jobEnvelopes[0].RunsOnValues).Count -ne 1 -or [string]$jobEnvelopes[0].RunsOnValues[0] -ne 'ubuntu-24.04' -or
            @($jobEnvelopes[0].TimeoutValues).Count -ne 1 -or [string]$jobEnvelopes[0].TimeoutValues[0] -ne '25') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' must be an unconditional ubuntu-24.04 job with exact direct properties and timeout-minutes: 25."
        }
        if ($jobEnvelopes.Count -eq 1) {
            Test-CanonicalWorkflowRunSteps -JobContent ([string]$jobEnvelopes[0].Content) -Errors $Errors
            $actualJobSha256 = ConvertTo-Sha256 -Value (([string]$jobEnvelopes[0].Content).TrimEnd())
            $expectedJobSha256 = [string](Get-OptionalProperty $requirement 'jobSha256' '')
            if ([string]::IsNullOrWhiteSpace($expectedJobSha256) -or $actualJobSha256 -ne $expectedJobSha256) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) job '$requiredJobName' differs from the exact reviewed job body."
            }
            $actualStepNames = @(Get-WorkflowStepNames -JobContent ([string]$jobEnvelopes[0].Content))
            $expectedStepNames = @(Get-CanonicalWorkflowStepNames -WorkflowPath ([string]$requirement.workflowPath))
            if (($actualStepNames -join '|') -cne ($expectedStepNames -join '|')) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) required job step names/order differ from the exact reviewed sequence."
            }
        }
        if ($workflowContent -match '(?mi)^\s*continue-on-error:\s*true\s*$' -or $workflowContent -match '(?mi)^\s*if:\s*(?:false|\$\{\{\s*false\s*\}\})\s*$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) contains a disabled or continue-on-error step that can hide a required gate."
        }
        if ($workflowContent -match '(?mi)^\s*pull_request_target\s*:' -or
            $workflowContent -match '(?mi)^\s*permissions:\s*(?:write-all|write)\s*$' -or
            $workflowContent -match '(?mi)^\s*shell:\s*bash\s+\{0\}\s*$' -or
            $workflowContent -match '(?m)(?:\|\|\s*true\s*$|^\s*set\s+\+e\s*$)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) weakens trigger, permissions, shell fail-fast, or command failure semantics."
        }
        foreach ($usesMatch in [regex]::Matches($workflowContent, '(?m)^\s*uses:\s*(?<value>[^\s#]+)')) {
            $usesValue = $usesMatch.Groups['value'].Value
            if ($usesValue -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) uses movable or non-canonical Action reference '$usesValue'."
            }
        }
        foreach ($dotnetLine in [regex]::Matches($workflowContent, '(?m)^\s*(?<command>dotnet\s+(?:restore|build|test)\b[^\r\n]*)$')) {
            if ($dotnetLine.Groups['command'].Value -notmatch '(?:^|\s)-noAutoResponse(?:\s|$)') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-BYPASS-RESPONSE" -Message "$($requirement.workflowPath) dotnet command does not disable automatic response files: $($dotnetLine.Groups['command'].Value.Trim())."
            }
        }
        if ($workflowContent -match '(?i)(?:IsTestProject|ImportDirectoryBuildTargets)\s*=\s*false' -or
            $workflowContent -match '(?i)(?:DirectoryBuildTargetsPath|DesignTimeBuild)\s*=') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) passes an MSBuild property that can bypass the required test gate."
        }
        if ($workflowContent -match '(?i)(?:--settings(?:\s|=)|RunSettingsFilePath|\.runsettings)') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) configures alternate VSTest runsettings and can override failSkips."
        }
        if ($workflowContent -notmatch '(?m)^\s*pull_request:\s*\{\}\s*$') {
            Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) must emit the required status for every pull request; path-filtered pull_request triggers are forbidden."
        }
        foreach ($requiredProject in @($requirement.requiredTestProjects)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?dotnet[ \t]+test[ \t]+' + [regex]::Escape([string]$requiredProject) + '(?=[ \t]|$)'
            $matches = [regex]::Matches($workflowContent, $pattern)
            if ($matches.Count -eq 0) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) does not schedule '$requiredProject'."
            }
        }
        $lastCommandPosition = -1
        foreach ($commandPrefix in @($requirement.requiredCommandPrefixes)) {
            $pattern = '(?m)^[ \t]*(?:run:[ \t]*)?' + [regex]::Escape([string]$commandPrefix) + '(?=[ \t]|$)'
            $match = [regex]::Match($workflowContent, $pattern)
            if (-not $match.Success) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) is missing required command '$commandPrefix'."
            } elseif ($match.Index -le $lastCommandPosition) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-CI" -Message "$($requirement.workflowPath) schedules '$commandPrefix' out of governance order."
            } else {
                $lastCommandPosition = $match.Index
            }
        }
    }
}

function Test-ProjectSnapshot {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $projectEntries = @($Baseline.projects | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    if ($projectEntries.Count -ne 1) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "snapshot project '$($Snapshot.projectPath)' does not map to exactly one baseline project."
        return
    }
    $project = $projectEntries[0]
    if ([string]$project.projectName -ne [string]$Snapshot.projectName) {
        Add-PolicyError -Errors $Errors -Code "$ruleId-BASELINE" -Message "project name mismatch for $($Snapshot.projectPath): current=$($Snapshot.projectName), baseline=$($project.projectName)."
    }

    $baselineById = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($test in @($project.tests)) { $baselineById[[string]$test.id] = $test }
    $currentIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $deltas = [System.Collections.Generic.List[object]]::new()
    foreach ($test in @($Snapshot.tests)) {
        if (-not $currentIds.Add([string]$test.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-SCAN" -Message "duplicate current test id '$($test.id)'."
            continue
        }
        if (-not $baselineById.ContainsKey([string]$test.id)) {
            if ([string]$test.testAttributeType -eq 'IIoT.EndToEndTests.WorkspaceAlignmentFactAttribute') {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WORKSPACE-ALIGNMENT" -Message "the reviewed WorkspaceAlignmentFact attribute cannot be used by another test: $($test.symbol)."
            }
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Add'; Test = $test })
            continue
        }
        $baselineTest = $baselineById[[string]$test.id]
        if ([string]$test.testAttributeType -ne [string]$baselineTest.testAttributeType -or
            [string]$test.attributeCategory -ne [string]$baselineTest.attributeCategory -or
            [string](Get-OptionalProperty $test.testAttributePolicy 'signature' '') -ne [string](Get-OptionalProperty $baselineTest.testAttributePolicy 'signature' '')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'AttributeChange'; Test = $test })
        }
        if ((Get-TestTraitSignature -Test $test) -cne (Get-TestTraitSignature -Test $baselineTest)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'TraitChange'; Test = $test })
        }
        if ([int]$test.inlineDataRows -gt [int]$baselineTest.inlineDataRows) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataIncrease'; Test = $test })
        } elseif ((@($test.inlineDataSignatures) -join '|') -cne (@($baselineTest.inlineDataSignatures) -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataChange'; Test = $test })
        }
        foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $test)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'InlineDataRemoval'; Test = $inlineDataRemoval })
        }
        $oldDynamicSources = @($baselineTest.dynamicDataSources)
        $newDynamicSources = @($test.dynamicDataSources)
        if (($newDynamicSources -join '|') -cne ($oldDynamicSources -join '|')) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'DynamicDataSourceChange'; Test = $test })
        }
        $baselineExecutionIds = @($baselineTest.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($test.executionTypes | Where-Object { [string]$_.id -notin $baselineExecutionIds })) {
            $executionDeltaTest = $test.PSObject.Copy()
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeIncrease'; Test = $executionDeltaTest })
        }
        $currentExecutionIds = @($test.executionTypes | ForEach-Object { [string]$_.id })
        foreach ($executionType in @($baselineTest.executionTypes | Where-Object { [string]$_.id -notin $currentExecutionIds })) {
            $executionDeltaTest = $baselineTest.PSObject.Copy()
            $executionDeltaTest.id = [string]$executionType.id
            $executionDeltaTest | Add-Member -NotePropertyName declarationId -NotePropertyValue ([string]$baselineTest.id) -Force
            $executionDeltaTest | Add-Member -NotePropertyName projectedCasesLost -NotePropertyValue (Get-ProjectedCasesPerExecution -Test $baselineTest) -Force
            $executionDeltaTest.executionType = [string]$executionType.name
            $executionDeltaTest.traits = $executionType.traits
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ExecutionTypeDecrease'; Test = $executionDeltaTest })
        }
        $projectedCaseDecrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $test
        if ($null -ne $projectedCaseDecrease) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'ProjectedCaseDecrease'; Test = $projectedCaseDecrease })
        }
    }

    $projectWaivers = @($WaiverManifest.waivers | Where-Object { $_.projectPath -eq $Snapshot.projectPath })
    $usedWaiverIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($baselineTest in @($project.tests | Where-Object { [string]$_.id -notin $currentIds })) {
        if ([bool]$project.protectBaselineRemovals -or (Test-IsFrozenTest -Project $project -Test $baselineTest)) {
            $deltas.Add([pscustomobject]@{ ChangeKind = 'Remove'; Test = $baselineTest })
        }
    }
    foreach ($delta in $deltas) {
        $test = $delta.Test
        $location = "$($Snapshot.projectPath) :: $($test.symbol) [$($test.id)]"
        if ($delta.ChangeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval')) {
            $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
            if ($matchingWaivers.Count -ne 1) {
                $lossCode = switch ($delta.ChangeKind) {
                    'Remove' { "$ruleId-REMOVAL" }
                    'InlineDataRemoval' { "$ruleId-INLINE-DATA" }
                    default { "$ruleId-CASE-DECREASE" }
                }
                Add-PolicyError -Errors $Errors -Code $lossCode -Message "$location is a protected '$($delta.ChangeKind)' and requires one exact verified-migration waiver."
            } else {
                [void]$usedWaiverIds.Add([string]$matchingWaivers[0].id)
            }
            continue
        }
        Test-GovernedTestMetadataAndRoute -Test $test -Baseline $Baseline -Project $project -Errors $Errors -Location $location
        $testKind = @(Get-TraitValues -Traits $test.traits -Name 'TestKind')
        $runtime = @(Get-TraitValues -Traits $test.traits -Name 'Runtime')

        if ($delta.ChangeKind -ne 'ExecutionTypeIncrease') {
            foreach ($executionType in @($test.executionTypes)) {
                $executionRouteTest = $test.PSObject.Copy()
                $executionRouteTest.executionType = [string]$executionType.name
                $executionRouteTest.traits = $executionType.traits
                $executionLocation = "$location :: execution=$([string]$executionType.name)"
                Test-GovernedTestMetadataAndRoute -Test $executionRouteTest -Baseline $Baseline -Project $project -Errors $Errors -Location $executionLocation
            }
        }

        $isFrozen = Test-IsFrozenTest -Project $project -Test $test
        if (-not $isFrozen) { continue }
        if ([string]$project.freezeMode -in @('All', 'Structure') -and $delta.ChangeKind -in @(
            'Add',
            'AttributeChange',
            'TraitChange',
            'InlineDataIncrease',
            'InlineDataChange',
            'DynamicDataSourceChange',
            'ExecutionTypeIncrease'
        )) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location cannot change the Phase 0 frozen test structure; use a trusted baseline transition."
            continue
        }

        $matchingWaivers = @($projectWaivers | Where-Object { $_.symbol -eq $test.id -and $_.changeKind -eq $delta.ChangeKind })
        if ($matchingWaivers.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-FROZEN" -Message "$location is frozen for '$($delta.ChangeKind)' and requires one exact waiver."
            continue
        }
        $waiver = $matchingWaivers[0]
        [void]$usedWaiverIds.Add([string]$waiver.id)
        $owner = @(Get-TraitValues -Traits $test.traits -Name 'Owner')
        if ($testKind.Count -ne 1 -or [string]$waiver.testKind -ne $testKind[0] -or $owner.Count -ne 1 -or [string]$waiver.owner -ne $owner[0]) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "waiver '$($waiver.id)' does not match the test TestKind/Owner metadata."
        }
    }

    foreach ($waiver in $projectWaivers) {
        if (-not $usedWaiverIds.Contains([string]$waiver.id)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "stale waiver '$($waiver.id)' matches no current frozen delta."
        }
    }

    $removedCount = @($project.tests | Where-Object { $_.id -notin $currentIds }).Count
    Write-Host "Validated $($Snapshot.projectName): current=$(@($Snapshot.tests).Count), new/expanded=$($deltas.Count), removed=$removedCount"
}

function Test-RepositorySnapshotPolicies {
    param(
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][object]$SnapshotsByProject,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Errors
    )

    $seenLogicalIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $seenRegressionIds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $protectedCaseLosses = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)

    foreach ($projectPathValue in @($SnapshotsByProject.Keys | Sort-Object)) {
        $snapshot = $SnapshotsByProject[$projectPathValue]
        foreach ($test in @($snapshot.tests)) {
            if ($seenLogicalIds.ContainsKey([string]$test.logicalId) -and $seenLogicalIds[[string]$test.logicalId] -ne [string]$snapshot.projectPath) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-DUPLICATE" -Message "logical declaration '$($test.symbol)' exists in both '$($seenLogicalIds[[string]$test.logicalId])' and '$($snapshot.projectPath)'."
            } else {
                $seenLogicalIds[[string]$test.logicalId] = [string]$snapshot.projectPath
            }

            $regressionIds = @(Get-TraitValues -Traits $test.traits -Name 'RegressionId')
            if ($regressionIds.Count -gt 1) {
                Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "$($snapshot.projectPath) :: $($test.symbol) declares more than one RegressionId."
            } elseif ($regressionIds.Count -eq 1) {
                $regressionId = [string]$regressionIds[0]
                $location = "$($snapshot.projectPath) :: $($test.symbol)"
                if ($seenRegressionIds.ContainsKey($regressionId) -and $seenRegressionIds[$regressionId] -ne $location) {
                    Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "RegressionId '$regressionId' is duplicated by '$($seenRegressionIds[$regressionId])' and '$location'."
                } else {
                    $seenRegressionIds[$regressionId] = $location
                }
            }
        }

        $baselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $snapshot.projectPath })
        if ($baselineProject.Count -ne 1) { continue }
        foreach ($baselineTest in @($baselineProject[0].tests)) {
            $currentTest = @($snapshot.tests | Where-Object { $_.id -eq $baselineTest.id })
            if ($currentTest.Count -ne 1) { continue }
            $decrease = Get-ProjectedCaseDecreaseDeltaTest -BaselineTest $baselineTest -CurrentTest $currentTest[0]
            if ($null -ne $decrease) {
                $protectedCaseLosses[[string]$decrease.id] = $decrease
            }
            foreach ($inlineDataRemoval in @(Get-InlineDataRemovalDeltaTests -BaselineTest $baselineTest -CurrentTest $currentTest[0])) {
                $protectedCaseLosses[[string]$inlineDataRemoval.id] = $inlineDataRemoval
            }
        }
    }

    foreach ($waiver in @($WaiverManifest.waivers | Where-Object { $_.changeKind -in @('Remove', 'ExecutionTypeDecrease', 'ProjectedCaseDecrease', 'InlineDataRemoval') })) {
        $sourceProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.projectPath })
        $sourceTest = @()
        $sourceLogicalId = $null
        $projectedCasesLost = 0
        if ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'Remove') {
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $waiver.symbol })
            if ($sourceTest.Count -eq 1) {
                $sourceLogicalId = [string]$sourceTest[0].logicalId
                $projectedCasesLost = [int]$sourceTest[0].projectedCases
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -eq 'ExecutionTypeDecrease') {
            $sourceTest = @($sourceProject[0].tests | Where-Object {
                @($_.executionTypes | Where-Object { $_.id -eq $waiver.symbol }).Count -eq 1
            })
            if ($sourceTest.Count -eq 1) {
                $projectedCasesLost = Get-ProjectedCasesPerExecution -Test $sourceTest[0]
            }
        } elseif ($sourceProject.Count -eq 1 -and $waiver.changeKind -in @('ProjectedCaseDecrease', 'InlineDataRemoval') -and $protectedCaseLosses.ContainsKey([string]$waiver.symbol)) {
            $decrease = $protectedCaseLosses[[string]$waiver.symbol]
            $sourceTest = @($sourceProject[0].tests | Where-Object { $_.id -eq $decrease.declarationId })
            $projectedCasesLost = [int]$decrease.projectedCasesLost
        }

        if ($sourceTest.Count -ne 1 -or $projectedCasesLost -le 0) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' does not resolve one concrete baseline loss."
            continue
        }
        if (-not $SnapshotsByProject.ContainsKey([string]$waiver.targetProject)) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' target project was not scanned."
            continue
        }

        $targetBaselineProject = @($Baseline.projects | Where-Object { $_.projectPath -eq $waiver.targetProject })
        $targetMatches = @($SnapshotsByProject[[string]$waiver.targetProject].tests | Where-Object {
            $candidate = $_
            $regressionIds = @(Get-TraitValues -Traits $candidate.traits -Name 'RegressionId')
            $testKinds = @(Get-TraitValues -Traits $candidate.traits -Name 'TestKind')
            $owners = @(Get-TraitValues -Traits $candidate.traits -Name 'Owner')
            if ($regressionIds.Count -ne 1 -or $regressionIds[0] -ne [string]$waiver.regressionId -or
                $testKinds.Count -ne 1 -or $testKinds[0] -ne [string]$waiver.testKind -or
                $owners.Count -ne 1 -or $owners[0] -ne [string]$waiver.owner) {
                return $false
            }
            if ($waiver.changeKind -eq 'Remove' -and [string]$candidate.logicalId -ne $sourceLogicalId) {
                return $false
            }

            $targetBaselineTest = if ($targetBaselineProject.Count -eq 1) {
                @($targetBaselineProject[0].tests | Where-Object { $_.id -eq $candidate.id })
            } else { @() }
            $addedProjectedCases = if ($targetBaselineTest.Count -eq 0) {
                [int]$candidate.projectedCases
            } elseif ($targetBaselineTest.Count -eq 1) {
                [int]$candidate.projectedCases - [int]$targetBaselineTest[0].projectedCases
            } else { 0 }
            return $addedProjectedCases -ge $projectedCasesLost
        })
        if ($targetMatches.Count -ne 1) {
            Add-PolicyError -Errors $Errors -Code "$ruleId-WAIVER" -Message "migration waiver '$($waiver.id)' is not backed by one uniquely classified RegressionId '$($waiver.regressionId)' with at least $projectedCasesLost newly added case(s) in '$($waiver.targetProject)'."
        }
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [ValidateRange(1, 600)][int]$TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
    }
    [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))
    return [pscustomobject]@{
        ExitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        TimedOut = $timedOut
        StandardOutput = $stdoutTask.Result
        StandardError = $stderrTask.Result
    }
}

function Get-DotNetListedTests {
    param([Parameter(Mandatory)][string]$Output)

    $collect = $false
    $tests = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Output, '\r?\n')) {
        if ($line -match 'Tests are available\s*:|测试可用\s*:|Tests disponibles\s*:|Tests disponibles sont\s*:') {
            $collect = $true
            continue
        }
        if (-not $collect) { continue }
        if ($line -match '^\s{2,}\S') {
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^(Test Run|Total tests|Passed!|Failed!|警告|Warning)') {
                $tests.Add($trimmed)
            }
        }
    }
    return [string[]]@($tests)
}

function Get-NormalizedRunnerCases {
    param([Parameter(Mandatory)][string[]]$Cases)

    $normalized = [string[]]@($Cases | ForEach-Object { [string]$_ })
    [Array]::Sort($normalized, [StringComparer]::Ordinal)
    return $normalized
}

function Get-MissingRunnerCaseOccurrences {
    param(
        [Parameter(Mandatory)][string[]]$BaselineCases,
        [Parameter(Mandatory)][string[]]$CurrentCases
    )

    $baseline = [string[]]@($BaselineCases)
    $current = [string[]]@($CurrentCases)
    [Array]::Sort($baseline, [StringComparer]::Ordinal)
    [Array]::Sort($current, [StringComparer]::Ordinal)
    $missing = [System.Collections.Generic.List[string]]::new()
    $baselineIndex = 0
    $currentIndex = 0
    while ($baselineIndex -lt $baseline.Count) {
        if ($currentIndex -ge $current.Count) {
            $missing.Add($baseline[$baselineIndex])
            $baselineIndex++
            continue
        }
        $comparison = [StringComparer]::Ordinal.Compare($baseline[$baselineIndex], $current[$currentIndex])
        if ($comparison -eq 0) {
            $baselineIndex++
            $currentIndex++
        }
        elseif ($comparison -gt 0) {
            $currentIndex++
        }
        else {
            $missing.Add($baseline[$baselineIndex])
            $baselineIndex++
        }
    }
    return [string[]]$missing
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '../..')
} else {
    $RepositoryRoot = Get-NormalizedPath $RepositoryRoot
}
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $RepositoryRoot 'scripts/tests/baselines/cloud-test-governance.baseline.json'
}
if ([string]::IsNullOrWhiteSpace($WaiverPath)) {
    $WaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/cloud-test-governance.waivers.json'
}
$BaselinePath = Get-NormalizedPath $BaselinePath
$WaiverPath = Get-NormalizedPath $WaiverPath

if ($Mode -eq 'ValidateRunnerIdentityFixture') {
    $composedValue = [string][char]0x00e9
    $decomposedValue = "e$([char]0x0301)"
    $composed = @(Get-NormalizedRunnerCases -Cases @("Cloud.Tests.Unicode(`"$composedValue`")"))
    $decomposed = @(Get-NormalizedRunnerCases -Cases @("Cloud.Tests.Unicode(`"$decomposedValue`")"))
    if ($composed.Count -ne 1 -or $decomposed.Count -ne 1 -or
        [string]::Equals([string]$composed[0], [string]$decomposed[0], [StringComparison]::Ordinal) -or
        (ConvertTo-Sha256 -Value $composed[0]) -eq (ConvertTo-Sha256 -Value $decomposed[0])) {
        throw "$ruleId-DISCOVERY ordinally distinct composed/decomposed runner identities collided."
    }
    $slash = @(Get-NormalizedRunnerCases -Cases @('Cloud.Tests.Path("a/b")'))
    $backslash = @(Get-NormalizedRunnerCases -Cases @('Cloud.Tests.Path("a\b")'))
    if ([string]::Equals([string]$slash[0], [string]$backslash[0], [StringComparison]::Ordinal) -or
        (ConvertTo-Sha256 -Value $slash[0]) -eq (ConvertTo-Sha256 -Value $backslash[0])) {
        throw "$ruleId-DISCOVERY ordinally distinct slash/backslash runner identities collided."
    }
    $baselineRunnerCases = @('Cloud.Tests.A', 'Cloud.Tests.A', 'Cloud.Tests.B')
    $addedRunnerCases = @('Cloud.Tests.A', 'Cloud.Tests.A', 'Cloud.Tests.B', 'Cloud.Tests.C')
    $lostDuplicateRunnerCases = @('Cloud.Tests.A', 'Cloud.Tests.B', 'Cloud.Tests.C')
    $baselineRunnerDigest = ConvertTo-Sha256 -Value ((Get-NormalizedRunnerCases -Cases $baselineRunnerCases) -join "`n")
    $addedRunnerDigest = ConvertTo-Sha256 -Value ((Get-NormalizedRunnerCases -Cases $addedRunnerCases) -join "`n")
    if ($baselineRunnerDigest -ceq $addedRunnerDigest -or
        @(Get-MissingRunnerCaseOccurrences -BaselineCases $baselineRunnerCases -CurrentCases $lostDuplicateRunnerCases).Count -ne 1) {
        throw "$ruleId-DISCOVERY exact runner baseline must reject additions and detect removed duplicate occurrences."
    }
    Write-Host 'Cloud ordinal runner identity fixture passed.'
    exit 0
}

if ($Mode -eq 'ValidateAttributePayloadFixture') {
    if ([string]::IsNullOrWhiteSpace($AttributeFixtureAssemblyPath) -or
        -not (Test-Path -LiteralPath $AttributeFixtureAssemblyPath -PathType Leaf)) {
        throw "$ruleId-SCAN ValidateAttributePayloadFixture requires a compiled fixture assembly."
    }
    $resolvedFixturePath = Get-NormalizedPath $AttributeFixtureAssemblyPath
    $context = New-TestMetadataLoadContext -TestAssemblyPath $resolvedFixturePath
    try {
        $assembly = $context.LoadFromAssemblyPath($resolvedFixturePath)
        $fixtureType = $assembly.GetType('CloudGovernance.AttributePayloadFixture', $true)
        $signatures = [ordered]@{}
        foreach ($methodName in @(
            'Baseline', 'Identical', 'StringChanged', 'NullChanged', 'ArrayValueChanged', 'ArrayOrderChanged',
            'EnumChanged', 'TypeChanged', 'NamedPropertyChanged', 'NamedFieldChanged', 'ComposedUnicode',
            'DecomposedUnicode', 'EmptyString', 'EmbeddedNull', 'SupplementaryPlane'
        )) {
            $method = $fixtureType.GetMethod($methodName, [Reflection.BindingFlags]'Public,Static')
            $attributes = @([Reflection.CustomAttributeData]::GetCustomAttributes($method) | Where-Object { $_.AttributeType.FullName -eq 'CloudGovernance.PayloadAttribute' })
            if ($attributes.Count -ne 1) {
                throw "$ruleId-SCAN attribute fixture method '$methodName' must expose exactly one PayloadAttribute."
            }
            $signatures[$methodName] = Get-CloudTestCustomAttributeSignature -Attribute $attributes[0]
        }
        if ([string]$signatures.Baseline -ne [string]$signatures.Identical) {
            throw "$ruleId-SCAN identical CustomAttributeData payloads produced different signatures."
        }
        if ([string]$signatures.ComposedUnicode -eq [string]$signatures.DecomposedUnicode) {
            throw "$ruleId-SCAN ordinally distinct composed/decomposed Unicode payloads collided."
        }
        $changed = @($signatures.Keys | Where-Object { $_ -notin @('Baseline', 'Identical') } | ForEach-Object { [string]$signatures[$_] })
        if (@($changed | Where-Object { $_ -eq [string]$signatures.Baseline }).Count -gt 0 -or
            @($changed | Sort-Object -CaseSensitive -Unique).Count -ne $changed.Count -or
            @($signatures.Values | Where-Object { [string]$_ -notmatch '^cloud-cad-v1:[0-9a-f]{64}$' }).Count -gt 0) {
            throw "$ruleId-SCAN typed CustomAttributeData payload variations were not uniquely and canonically encoded."
        }
    }
    finally {
        $context.Dispose()
    }
    Write-Host 'Cloud typed CustomAttributeData payload fixture passed.'
    exit 0
}

if ($Mode -eq 'ValidateBaselineAnchor') {
    $canonicalBaselinePath = Get-NormalizedPath (Join-Path $RepositoryRoot $baselineRepositoryPath)
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $BaselinePath.Equals($canonicalBaselinePath, $pathComparison)) {
        throw "$ruleId-BASELINE ValidateBaselineAnchor only accepts the canonical repository baseline."
    }
    if ($TrustedBaseRevision -notmatch '^[0-9A-Fa-f]{40}$' -or $TrustedBaseRevision -match '^0{40}$') {
        throw "$ruleId-BASELINE ValidateBaselineAnchor requires one non-zero full 40-character trusted base revision."
    }
    $headRevisionResult = Invoke-CapturedProcess -FileName 'git' -Arguments @('rev-parse', 'HEAD') -WorkingDirectory $RepositoryRoot
    $headRevision = $headRevisionResult.StandardOutput.Trim()
    if ($headRevisionResult.TimedOut -or $headRevisionResult.ExitCode -ne 0 -or $headRevision -notmatch '^[0-9A-Fa-f]{40}$') {
        throw "$ruleId-BASELINE could not resolve the checked-out HEAD revision."
    }
    if ([string]::Equals($TrustedBaseRevision, $headRevision, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ruleId-BASELINE-SELF trusted base revision must differ from the candidate HEAD."
    }
    if (-not (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) {
        throw "$ruleId-BASELINE baseline does not exist: $BaselinePath"
    }
    $baseCommit = Invoke-CapturedProcess -FileName 'git' -Arguments @('cat-file', '-e', "$TrustedBaseRevision^{commit}") -WorkingDirectory $RepositoryRoot
    if ($baseCommit.TimedOut -or $baseCommit.ExitCode -ne 0) {
        throw "$ruleId-BASELINE trusted base revision is unavailable: $TrustedBaseRevision."
    }
    $ancestorArguments = if ($AnchorRelationship -eq 'HeadAncestorOfBase') {
        @('merge-base', '--is-ancestor', 'HEAD', $TrustedBaseRevision)
    } else {
        @('merge-base', '--is-ancestor', $TrustedBaseRevision, 'HEAD')
    }
    $ancestorCheck = Invoke-CapturedProcess -FileName 'git' -Arguments $ancestorArguments -WorkingDirectory $RepositoryRoot
    if ($ancestorCheck.TimedOut -or $ancestorCheck.ExitCode -ne 0) {
        throw "$ruleId-BASELINE checked-out commit violates anchor relationship '$AnchorRelationship' with $TrustedBaseRevision."
    }

    $currentBaselineText = (Get-Content -LiteralPath $BaselinePath -Raw).Replace("`r`n", "`n")
    $currentBaselineDigest = ConvertTo-Sha256 -Value $currentBaselineText
    $baseBaseline = Invoke-CapturedProcess -FileName 'git' -Arguments @('show', "${TrustedBaseRevision}:$baselineRepositoryPath") -WorkingDirectory $RepositoryRoot
    if (-not $baseBaseline.TimedOut -and $baseBaseline.ExitCode -eq 0) {
        $baseBaselineDigest = ConvertTo-Sha256 -Value $baseBaseline.StandardOutput.Replace("`r`n", "`n")
        if ($currentBaselineDigest -ne $baseBaselineDigest) {
            throw "$ruleId-BASELINE trusted baseline transition is forbidden in Phase 0: base=$baseBaselineDigest current=$currentBaselineDigest."
        }
        Write-Host "Cloud immutable baseline anchor passed: base=$TrustedBaseRevision digest=$currentBaselineDigest"
        exit 0
    }

    if ($TrustedBaseRevision.ToLowerInvariant() -ne $reviewedBaselineSourceHead) {
        throw "$ruleId-BASELINE trusted base has no reviewed baseline and is not the one-time Cloud genesis source $reviewedBaselineSourceHead."
    }
    $genesisCommitCount = Invoke-CapturedProcess -FileName 'git' -Arguments @('rev-list', '--count', "$TrustedBaseRevision..HEAD") -WorkingDirectory $RepositoryRoot
    if ($genesisCommitCount.TimedOut -or $genesisCommitCount.ExitCode -ne 0 -or $genesisCommitCount.StandardOutput.Trim() -cne '1') {
        throw "$ruleId-BASELINE one-time Cloud genesis must be the direct single child of $reviewedBaselineSourceHead."
    }
    $baselineCandidate = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -Depth 100
    if ([string](Get-OptionalProperty $baselineCandidate.provenance 'sourceHead' '') -ne $reviewedBaselineSourceHead) {
        throw "$ruleId-BASELINE genesis baseline does not identify the reviewed source head."
    }
    $genesisStructureErrors = [System.Collections.Generic.List[string]]::new()
    Test-BaselineStructure -Baseline $baselineCandidate -Errors $genesisStructureErrors
    Assert-NoPolicyErrors -Errors $genesisStructureErrors
    $changed = Invoke-CapturedProcess -FileName 'git' -Arguments @('-c', 'core.quotePath=false', 'diff', '--name-only', "$TrustedBaseRevision..HEAD") -WorkingDirectory $RepositoryRoot
    if ($changed.TimedOut -or $changed.ExitCode -ne 0) {
        throw "$ruleId-BASELINE could not inspect the one-time genesis transition."
    }
    $allowedGenesisPaths = @(
        '.gitattributes', '.github/CODEOWNERS', '.github/workflows/cloud-ci.yml', 'Directory.Build.props', 'Directory.Build.targets', 'global.json',
        'scripts/tests/CloudTestAttributeCodec.psm1',
        'scripts/tests/TestCloudTestGovernanceBehavior.ps1',
        'scripts/tests/TestCloudTestGovernancePolicy.ps1',
        'scripts/tests/baselines/cloud-test-governance.baseline.json',
        'scripts/tests/baselines/cloud-test-governance.waivers.json',
        'deploy/tests/deployment-behavior.sh',
        'src/tests/Directory.Build.props', 'src/tests/xunit.runner.json',
        'src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj',
        'docs/云端架构治理清单.md', 'docs/云端规则.md', 'docs/改动复盘与规则沉淀.md'
    )
    foreach ($changedPath in @($changed.StandardOutput -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (@($allowedGenesisPaths | Where-Object {
            $changedPath -ceq $_ -or ($_.EndsWith('/') -and $changedPath.StartsWith($_, [StringComparison]::Ordinal))
        }).Count -eq 0) {
            throw "$ruleId-BASELINE-GENESIS-PATH genesis transition includes an unreviewed source/build input: $changedPath."
        }
    }
    Write-Host "Cloud one-time baseline genesis anchor passed: source=$TrustedBaseRevision digest=$currentBaselineDigest"
    exit 0
}

if ($Mode -eq 'ValidateRunnerConfiguration') {
    if ([string]::IsNullOrWhiteSpace($RunnerConfigPath)) {
        throw "$ruleId-DISABLED ValidateRunnerConfiguration requires RunnerConfigPath."
    }
    $runnerErrors = [System.Collections.Generic.List[string]]::new()
    Test-RunnerConfigurationFile -ResolvedRunnerConfigPath (Get-NormalizedPath $RunnerConfigPath) -Errors $runnerErrors -Context 'built test output'
    Assert-NoPolicyErrors -Errors $runnerErrors
    Write-Host "Cloud failSkips runner configuration passed: $RunnerConfigPath"
    exit 0
}

if ($Mode -eq 'GenerateBaseline') {
    if (-not $AllowBaselineWrite) {
        throw "$ruleId-BASELINE baseline generation requires -AllowBaselineWrite and reviewed output."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CI)) {
        throw "$ruleId-BASELINE CI must never regenerate the reviewed baseline."
    }

    $specifications = if (-not [string]::IsNullOrWhiteSpace($ProjectPath) -or -not [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
            throw "$ruleId-BASELINE single-project generation requires ProjectPath, ProjectName, and AssemblyPath."
        }
        @([pscustomobject]@{
            ProjectPath = Get-NormalizedPath $ProjectPath
            ProjectName = $ProjectName
            AssemblyPath = Get-NormalizedPath $AssemblyPath
            AdditionalReferencePaths = if ([string]::IsNullOrWhiteSpace($ReferencePathsFile)) {
                [string[]]@()
            } else {
                [string[]]@(Get-Content -LiteralPath $ReferencePathsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            }
        })
    } else {
        @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)
    }

    $projects = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $specifications) {
        $snapshot = Get-TestAssemblySnapshot `
            -ResolvedProjectPath $specification.ProjectPath `
            -ResolvedProjectName $specification.ProjectName `
            -ResolvedAssemblyPath $specification.AssemblyPath `
            -AdditionalReferencePaths @((Get-OptionalProperty $specification 'AdditionalReferencePaths' @()))
        $projectFileForDiscovery = Get-RelativePath -BasePath $RepositoryRoot -Path $specification.ProjectPath
        $discoveryRun = Invoke-CapturedProcess -FileName 'dotnet' -Arguments @(
            'test', $projectFileForDiscovery, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests',
            '--disable-build-servers', '--nologo', '-noAutoResponse'
        ) -WorkingDirectory $RepositoryRoot
        if ($discoveryRun.TimedOut -or $discoveryRun.ExitCode -ne 0) {
            throw "$ruleId-DISCOVERY baseline generation could not list $($specification.ProjectName): $($discoveryRun.StandardError.Trim())"
        }
        $runnerCases = @(Get-NormalizedRunnerCases -Cases (Get-DotNetListedTests -Output $discoveryRun.StandardOutput))
        $policy = Get-GeneratedProjectPolicy -GeneratedProjectName $specification.ProjectName -GeneratedProjectPath $specification.ProjectPath
        $projects.Add([pscustomobject][ordered]@{
            projectPath = $snapshot.projectPath
            projectName = $snapshot.projectName
            isLegacy = $policy.isLegacy
            freezeMode = $policy.freezeMode
            frozenTypePatterns = $policy.frozenTypePatterns
            frozenSourceFiles = $policy.frozenSourceFiles
            frozenSourceHashes = $policy.frozenSourceHashes
            allowedNewTestKinds = $policy.allowedNewTestKinds
            allowedNewRuntimes = $policy.allowedNewRuntimes
            forbiddenNewTestKinds = $policy.forbiddenNewTestKinds
            discoveryCeilings = $policy.discoveryCeilings
            protectBaselineRemovals = $policy.protectBaselineRemovals
            sourceAssemblySha256 = $snapshot.assemblySha256
            baselineDeclarations = $snapshot.declarations
            baselineExecutionTemplates = $snapshot.executionTemplates
            baselineProjectedCases = $snapshot.projectedCases
            baselineRunnerCases = $runnerCases.Count
            runnerCaseDigest = ConvertTo-Sha256 -Value ($runnerCases -join "`n")
            runnerCases = [string[]]$runnerCases
            tests = $snapshot.tests
        })
    }
    $projectPaths = [string[]]@($projects | ForEach-Object { $_.projectPath } | Sort-Object -Unique)
    $protectedAssets = @(Get-FileContentManifest -Root $RepositoryRoot -RelativePaths (Get-CanonicalProtectedAssetPaths))
    $workflowManifestEntries = @(Get-WorkflowManifestEntries -Root $RepositoryRoot)
    $projectManifestEntries = @(Get-ProjectManifestEntries -Root $RepositoryRoot)
    $buildControlManifestEntries = @(Get-BuildControlManifestEntries -Root $RepositoryRoot)
    $restoreControlManifestEntries = @(Get-RestoreControlManifestEntries -Root $RepositoryRoot)
    $frontendTestManifestEntries = @(Get-FrontendUnitTestManifestEntries -Root $RepositoryRoot)
    $workflowPathValue = '.github/workflows/cloud-ci.yml'
    $workflowContent = (Get-Content -LiteralPath (Join-Path $RepositoryRoot $workflowPathValue) -Raw).Replace('\', '/')
    $jobEnvelopes = @(Get-WorkflowJobEnvelope -WorkflowContent $workflowContent -JobName 'build-test')
    if ($jobEnvelopes.Count -ne 1) {
        throw "$ruleId-CI baseline generation requires exactly one build-test job."
    }
    $scannerHashesByPlatform = [ordered]@{}
    foreach ($platform in @(Get-OrdinalSortedUniqueStrings -Values @($approvedMetadataLoadContextSha256ByPlatform.Keys))) {
        $scannerHashesByPlatform[$platform] = [string]$approvedMetadataLoadContextSha256ByPlatform[$platform]
    }
    $baseline = [pscustomobject][ordered]@{
        schemaVersion = $baselineSchemaVersion
        ruleId = $ruleId
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        provenance = [pscustomobject][ordered]@{
            sourceHead = $reviewedBaselineSourceHead
            baselineStatus = 'ReviewedSource'
            note = 'The sourceHead is the independently reviewed pre-governance Cloud source snapshot. Remote required-check and independent Code Owner enforcement remain external closure items.'
        }
        attributeSignatureSchema = 'cloud-cad-v1'
        scanner = [pscustomobject]@{
            engine = 'System.Reflection.MetadataLoadContext'
            activeDotnetSdk = (& dotnet --version | Out-String).Trim()
            metadataLoadContextSha256ByPlatform = [pscustomobject]$scannerHashesByPlatform
        }
        protectedAssets = [object[]]$protectedAssets
        workflowManifest = [pscustomobject][ordered]@{
            count = $workflowManifestEntries.Count
            sha256 = Get-ManifestDigest -Entries $workflowManifestEntries
        }
        projectManifest = [pscustomobject][ordered]@{
            count = $projectManifestEntries.Count
            sha256 = Get-ManifestDigest -Entries $projectManifestEntries
        }
        buildControlManifest = [pscustomobject][ordered]@{
            count = $buildControlManifestEntries.Count
            sha256 = Get-ManifestDigest -Entries $buildControlManifestEntries
        }
        restoreControlManifest = [pscustomobject][ordered]@{
            count = $restoreControlManifestEntries.Count
            sha256 = Get-ManifestDigest -Entries $restoreControlManifestEntries
        }
        frontendTestManifest = [pscustomobject][ordered]@{
            count = $frontendTestManifestEntries.Count
            sha256 = Get-ManifestDigest -Entries $frontendTestManifestEntries
            runnerCases = 67
        }
        deploymentBehavior = [pscustomobject][ordered]@{
            sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'deploy/tests/deployment-behavior.sh') -Algorithm SHA256).Hash.ToLowerInvariant()
            runnerCases = 33
        }
        allowedMetadata = [pscustomobject]@{
            testKinds = $allowedTestKinds
            runtimes = $allowedRuntimes
            risks = $allowedRisks
            owners = $allowedOwners
            capabilities = $allowedCapabilities
        }
        ciRequirements = @(
            [pscustomobject]@{
                workflowPath = $workflowPathValue
                jobSha256 = ConvertTo-Sha256 -Value (([string]$jobEnvelopes[0].Content).TrimEnd())
                requiredTestProjects = $projectPaths
                requiredCommandPrefixes = Get-CanonicalRequiredCommandPrefixes
            }
        )
        projects = [object[]]@($projects | Sort-Object projectPath)
    }
    Write-JsonAtomically -Value $baseline -Path $BaselinePath
    Write-Host "Generated reviewed baseline candidate: $BaselinePath"
    Write-Host "Projects: $($projects.Count)"
    Write-Host "Expanded declarations: $(($projects | Measure-Object -Property baselineDeclarations -Sum).Sum)"
    Write-Host "Execution templates: $(($projects | Measure-Object -Property baselineExecutionTemplates -Sum).Sum)"
    Write-Host "Projected cases: $(($projects | Measure-Object -Property baselineProjectedCases -Sum).Sum)"
    exit 0
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "$ruleId-BASELINE baseline does not exist: $BaselinePath"
}
if (-not (Test-Path $WaiverPath -PathType Leaf)) {
    throw "$ruleId-WAIVER waiver manifest does not exist: $WaiverPath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 100
$waiverManifest = Get-Content $WaiverPath -Raw | ConvertFrom-Json -Depth 100
$errors = [System.Collections.Generic.List[string]]::new()

if ($Mode -eq 'ValidateWorkflowFixture') {
    if ([string]::IsNullOrWhiteSpace($WorkflowFixturePath) -or
        -not (Test-Path -LiteralPath $WorkflowFixturePath -PathType Leaf)) {
        throw "$ruleId-CI ValidateWorkflowFixture requires one workflow fixture file."
    }
    $requirements = @($baseline.ciRequirements)
    if ($requirements.Count -ne 1) {
        throw "$ruleId-CI ValidateWorkflowFixture requires exactly one reviewed Cloud workflow requirement."
    }
    Test-WorkflowDocumentSemantics `
        -WorkflowContent (Get-Content -LiteralPath $WorkflowFixturePath -Raw) `
        -Requirement $requirements[0] `
        -Errors $errors `
        -Context 'workflow semantic fixture'
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Cloud workflow semantic fixture passed.'
    exit 0
}

if ($Mode -eq 'ValidateSnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $snapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic Cloud test governance snapshot passed.'
    exit 0
}

if ($Mode -eq 'ValidateRepositorySnapshot') {
    Test-BaselineStructure -Baseline $baseline -Errors $errors -AllowSyntheticPolicy
    Test-WaiverManifest -WaiverManifest $waiverManifest -Baseline $baseline -Errors $errors
    if (-not (Test-Path $CurrentSnapshotPath -PathType Leaf)) {
        Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "repository snapshot does not exist: $CurrentSnapshotPath"
    } else {
        $repositorySnapshot = Get-Content $CurrentSnapshotPath -Raw | ConvertFrom-Json -Depth 100
        $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
        foreach ($snapshot in @((Get-OptionalProperty $repositorySnapshot 'snapshots' @()))) {
            if ($snapshotsByProject.ContainsKey([string]$snapshot.projectPath)) {
                Add-PolicyError -Errors $errors -Code "$ruleId-SCAN" -Message "duplicate repository snapshot for '$($snapshot.projectPath)'."
                continue
            }
            $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
            Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
        }
        Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Synthetic Cloud repository migration snapshot passed.'
    exit 0
}

Test-StaticPolicy -Baseline $baseline -WaiverManifest $waiverManifest -Errors $errors
if ($Mode -eq 'ValidateStatic') {
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Cloud test governance static policy passed.'
    exit 0
}

if ($Mode -eq 'ValidateDiscovery') {
    foreach ($project in @($baseline.projects)) {
        $projectFile = Join-Path $RepositoryRoot $project.projectPath
        $specification = @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration | Where-Object { $_.ProjectPath -eq (Get-NormalizedPath $projectFile) })
        if ($specification.Count -ne 1) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) cannot resolve one built test assembly."
            continue
        }
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification[0].RunnerConfigPath -Errors $errors -Context $project.projectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification[0].ProjectPath -ResolvedProjectName $specification[0].ProjectName -ResolvedAssemblyPath $specification[0].AssemblyPath
        $arguments = @('test', $projectFile, '-c', $Configuration, '--no-build', '--no-restore', '--list-tests', '--disable-build-servers', '--nologo', '-noAutoResponse')
        $run = Invoke-CapturedProcess -FileName 'dotnet' -Arguments $arguments -WorkingDirectory $RepositoryRoot
        if ($run.TimedOut) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests exceeded the 120-second hard timeout."
            continue
        }
        if ($run.ExitCode -ne 0) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) list-tests failed: $($run.StandardError.Trim())."
            continue
        }
        $listedTests = @(Get-DotNetListedTests -Output $run.StandardOutput)
        $normalizedRunnerCases = @(Get-NormalizedRunnerCases -Cases $listedTests)
        $runnerDigest = ConvertTo-Sha256 -Value ($normalizedRunnerCases -join "`n")
        if ($listedTests.Count -ne [int]$snapshot.projectedCases -or
            $listedTests.Count -ne [int]$project.baselineRunnerCases -or
            $runnerDigest -cne [string]$project.runnerCaseDigest) {
            Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) runner discovery differs from the exact reviewed roster: actual=$($listedTests.Count), projected=$($snapshot.projectedCases), baseline=$($project.baselineRunnerCases), currentDigest=$runnerDigest, baselineDigest=$($project.runnerCaseDigest)."
        } else {
            Write-Host "Discovery reconciliation $($project.projectName): actual=$($listedTests.Count), projected=$($snapshot.projectedCases), baseline=$($project.baselineRunnerCases), digest=$runnerDigest"
        }
        foreach ($ceiling in @($project.discoveryCeilings)) {
            $filter = [string]$ceiling.displayNameContains
            $matchingTests = if ([string]::IsNullOrWhiteSpace($filter)) { $listedTests } else { @($listedTests | Where-Object { $_.Contains($filter, [StringComparison]::Ordinal) }) }
            if ($matchingTests.Count -eq 0) {
                Add-PolicyError -Errors $errors -Code "$ruleId-DISCOVERY" -Message "$($project.projectName) discovery ceiling '$filter' matched zero tests."
            } elseif ($matchingTests.Count -gt [int]$ceiling.maximum) {
                Add-PolicyError -Errors $errors -Code "$ruleId-FROZEN" -Message "$($project.projectName) discovery ceiling '$filter' grew to $($matchingTests.Count), maximum=$($ceiling.maximum)."
            } else {
                Write-Host "Discovery ceiling $($project.projectName) '$filter': $($matchingTests.Count)/$($ceiling.maximum)"
            }
        }
    }
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Cloud legacy discovery ceilings passed.'
    exit 0
}

if ($Mode -eq 'ValidateProject') {
    if ([string]::IsNullOrWhiteSpace($ProjectPath) -or [string]::IsNullOrWhiteSpace($ProjectName) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        throw "$ruleId-SCAN ValidateProject requires ProjectPath, ProjectName, and AssemblyPath."
    }
    $additionalReferencePaths = if ([string]::IsNullOrWhiteSpace($ReferencePathsFile)) {
        @()
    } elseif (-not (Test-Path $ReferencePathsFile -PathType Leaf)) {
        throw "$ruleId-SCAN reference-path response file does not exist: $ReferencePathsFile"
    } else {
        [string[]]@(Get-Content $ReferencePathsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath (Get-NormalizedPath $ProjectPath) -ResolvedProjectName $ProjectName -ResolvedAssemblyPath (Get-NormalizedPath $AssemblyPath) -AdditionalReferencePaths $additionalReferencePaths
    Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host "Cloud test governance assembly policy passed: $ProjectName"
    exit 0
}

if ($Mode -eq 'ValidateRepository') {
    $snapshotsByProject = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($specification in @(Get-TestProjectSpecifications -RequestedConfiguration $Configuration)) {
        Test-RunnerConfigurationFile -ResolvedRunnerConfigPath $specification.RunnerConfigPath -Errors $errors -Context $specification.ProjectName
        $snapshot = Get-TestAssemblySnapshot -ResolvedProjectPath $specification.ProjectPath -ResolvedProjectName $specification.ProjectName -ResolvedAssemblyPath $specification.AssemblyPath
        $snapshotsByProject[[string]$snapshot.projectPath] = $snapshot
        Test-ProjectSnapshot -Baseline $baseline -WaiverManifest $waiverManifest -Snapshot $snapshot -Errors $errors
    }
    Test-RepositorySnapshotPolicies -Baseline $baseline -WaiverManifest $waiverManifest -SnapshotsByProject $snapshotsByProject -Errors $errors
    Assert-NoPolicyErrors -Errors $errors
    Write-Host 'Cloud test governance repository policy passed.'
    exit 0
}

throw "$ruleId unsupported mode '$Mode'."
