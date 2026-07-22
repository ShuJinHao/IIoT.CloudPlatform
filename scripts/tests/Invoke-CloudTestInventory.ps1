[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Required', 'EndToEnd', 'WorkspaceAlignment')]
    [string]$Mode = 'Required',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [switch]$CollectCoverage,
    [string]$ResultsDirectory = 'artifacts/test-results'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$cloudArchitectureAnalyzerProject = [System.IO.Path]::GetFullPath(
    (Join-Path $repoRoot 'src/analyzers/IIoT.CloudPlatform.Analyzers/IIoT.CloudPlatform.Analyzers.csproj'))
$manifestPath = Join-Path $repoRoot 'src/tests/cloud-test-inventory.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -Depth 100
if ([int]$manifest.schemaVersion -ne 2) {
    throw "Cloud test inventory schemaVersion must be 2, actual=$($manifest.schemaVersion)."
}
$resultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Resolve-RepoPath([string]$relativePath) {
    return Join-Path $repoRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-DirectProjectProperty([xml]$project, [string]$propertyName, [string]$projectPath) {
    $nodes = @($project.SelectNodes("/Project/PropertyGroup/$propertyName"))
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$nodes[0].InnerText)) {
        throw "Test project must declare exactly one direct $propertyName property: $projectPath"
    }
    return ([string]$nodes[0].InnerText).Trim()
}

$evaluatedProjectCache = @{}

function Get-EvaluatedProject {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$ProjectConfiguration = $Configuration
    )

    $fullPath = [System.IO.Path]::GetFullPath($ProjectPath)
    $cacheKey = "$ProjectConfiguration|$fullPath"
    if ($evaluatedProjectCache.ContainsKey($cacheKey)) {
        return $evaluatedProjectCache[$cacheKey]
    }
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        throw "Evaluated project does not exist: $fullPath"
    }

    $arguments = @(
        'msbuild', $fullPath,
        '-nologo',
        "-property:Configuration=$ProjectConfiguration",
        '-getItem:ProjectReference',
        '-getItem:PackageReference',
        '-getItem:Compile',
        '-getProperty:IsTestProject',
        '-getProperty:AssemblyName',
        '-getProperty:MSBuildProjectName',
        '-getProperty:CloudTestKind',
        '-getProperty:CloudTestRuntime',
        '-getProperty:CloudTestExecutionGroup',
        '-getProperty:CloudTestRequired',
        '-getProperty:TargetFramework',
        '-getProperty:TargetFrameworks',
        '-getProperty:RunAnalyzers',
        '-getProperty:RunAnalyzersDuringBuild',
        '-getProperty:NoWarn',
        '-getProperty:MSBuildAllProjects',
        '-getProperty:TargetPath',
        '-getProperty:TargetDir',
        '-getProperty:DebugType',
        '-noAutoResponse'
    )
    $output = @(& dotnet @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Evaluated MSBuild query failed for ${fullPath}:`n$($output -join [Environment]::NewLine)"
    }

    try {
        $document = ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Evaluated MSBuild query returned invalid JSON for ${fullPath}: $($_.Exception.Message)"
    }
    if ($null -eq $document.Properties -or $null -eq $document.Items) {
        throw "Evaluated MSBuild query returned an incomplete document for $fullPath."
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$document.Properties.TargetFrameworks)) {
        throw "Multi-target project evaluation is unsupported and must fail closed until every target framework is reconciled explicitly: project=$fullPath targetFrameworks=$($document.Properties.TargetFrameworks)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$document.Properties.TargetFramework)) {
        throw "Evaluated project has no single TargetFramework: $fullPath"
    }

    $projectReferences = @(
        @($document.Items.ProjectReference) | ForEach-Object {
            $fullReferencePath = [string]$_.FullPath
            if ([string]::IsNullOrWhiteSpace($fullReferencePath)) {
                throw "Evaluated ProjectReference has no FullPath: project=$fullPath identity=$($_.Identity)"
            }
            [pscustomobject]@{
                FullPath = [System.IO.Path]::GetFullPath($fullReferencePath)
                OutputItemType = if ($null -ne $_.PSObject.Properties['OutputItemType']) { [string]$_.OutputItemType } else { '' }
                ReferenceOutputAssembly = if ($null -ne $_.PSObject.Properties['ReferenceOutputAssembly']) { [string]$_.ReferenceOutputAssembly } else { '' }
                IsAspireProjectResource = if ($null -ne $_.PSObject.Properties['IsAspireProjectResource']) { [string]$_.IsAspireProjectResource } else { '' }
                DefiningProjectFullPath = if ($null -ne $_.PSObject.Properties['DefiningProjectFullPath']) { [string]$_.DefiningProjectFullPath } else { '' }
            }
        }
    )
    $references = @($projectReferences | ForEach-Object FullPath | Sort-Object -Unique)
    $packages = @(
        @($document.Items.PackageReference) |
            ForEach-Object { [string]$_.Identity } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    $compiles = @(
        @($document.Items.Compile) | ForEach-Object {
            $link = if ($null -ne $_.PSObject.Properties['Link']) {
                [string]$_.Link
            } else {
                ''
            }
            [pscustomobject]@{
                FullPath = [System.IO.Path]::GetFullPath([string]$_.FullPath)
                Link = $link
            }
        }
    )
    $evaluation = [pscustomobject]@{
        ProjectPath = $fullPath
        Configuration = $ProjectConfiguration
        References = $references
        ProjectReferences = $projectReferences
        Packages = $packages
        Compiles = $compiles
        Properties = $document.Properties
    }
    $evaluatedProjectCache[$cacheKey] = $evaluation
    return $evaluation
}

function Assert-ProjectCompileOwnership {
    param(
        [Parameter(Mandatory)]$Evaluation,
        [Parameter(Mandatory)][string]$Label
    )

    $projectDirectory = [System.IO.Path]::GetDirectoryName([string]$Evaluation.ProjectPath)
    $directoryPrefix = $projectDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($compile in @($Evaluation.Compiles)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$compile.Link) -or
            -not ([string]$compile.FullPath).StartsWith($directoryPrefix, [StringComparison]::Ordinal)) {
            throw "$Label compiles linked or cross-project source: path=$($compile.FullPath) link=$($compile.Link)"
        }
    }
}

function Assert-ProductionAnalyzerEnforcement {
    param(
        [Parameter(Mandatory)]$Evaluation,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not [string]::Equals(
            [string]$Evaluation.Properties.RunAnalyzers,
            'true',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$Evaluation.Properties.RunAnalyzersDuringBuild,
            'true',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label disables required architecture analyzer execution: RunAnalyzers=$($Evaluation.Properties.RunAnalyzers) RunAnalyzersDuringBuild=$($Evaluation.Properties.RunAnalyzersDuringBuild)"
    }
    if ([string]$Evaluation.Properties.NoWarn -match '(?i)CLOUDARCH\d{3}') {
        throw "$Label attempts to suppress a non-configurable Cloud architecture diagnostic through NoWarn."
    }
    $requiredReferences = @($Evaluation.ProjectReferences | Where-Object {
        [string]::Equals(
            [string]$_.FullPath,
            $cloudArchitectureAnalyzerProject,
            [StringComparison]::OrdinalIgnoreCase)
    })
    if ($requiredReferences.Count -ne 1 -or
        [string]$requiredReferences[0].OutputItemType -cne 'Analyzer' -or
        -not [string]::Equals([string]$requiredReferences[0].ReferenceOutputAssembly, 'false', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$requiredReferences[0].IsAspireProjectResource, 'false', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label does not carry exactly one evaluated Cloud architecture Analyzer reference with non-runtime metadata."
    }
}

function Assert-ProductionPackageBoundary {
    param(
        [Parameter(Mandatory)]$Evaluation,
        [Parameter(Mandatory)][string]$Label
    )

    $testPackages = @($Evaluation.Packages | Where-Object {
        $_ -match '(?i)^(?:Microsoft\.NET\.Test\.Sdk|xunit(?:\.|$)|nunit(?:\.|$)|Moq$|FluentAssertions$|coverlet(?:\.|$)|NSubstitute$|FakeItEasy$)'
    })
    if ($testPackages.Count -gt 0) {
        throw "$Label imports test-only packages through the evaluated graph: $($testPackages -join ',')"
    }
}

$dynamicBuildGraphXPath = '//*[local-name()="Target"]//*[local-name()="Compile" or local-name()="Reference" or local-name()="ProjectReference" or local-name()="PackageReference" or local-name()="Analyzer" or local-name()="MSBuild" or local-name()="Csc"]'
$evaluatedImportCache = @{}

function Get-EvaluatedImportPaths {
    param([Parameter(Mandatory)]$Evaluation)

    $cacheKey = "$($Evaluation.Configuration)|$($Evaluation.ProjectPath)"
    if ($evaluatedImportCache.ContainsKey($cacheKey)) {
        return @($evaluatedImportCache[$cacheKey])
    }

    $preprocessedPath = Join-Path (
        [System.IO.Path]::GetTempPath()) "cloud-msbuild-preprocessed-$([Guid]::NewGuid().ToString('N')).xml"
    try {
        $arguments = @(
            'msbuild', [string]$Evaluation.ProjectPath,
            '-nologo',
            "-property:Configuration=$($Evaluation.Configuration)",
            "-preprocess:$preprocessedPath",
            '-noAutoResponse'
        )
        $output = @(& dotnet @arguments 2>&1)
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $preprocessedPath -PathType Leaf)) {
            throw "Evaluated MSBuild preprocess query failed for $($Evaluation.ProjectPath):`n$($output -join [Environment]::NewLine)"
        }

        $preprocessed = Get-Content $preprocessedPath -Raw
        $segments = [regex]::Split($preprocessed, '(?m)^\s*={40,}\s*$')
        $paths = @($segments | ForEach-Object {
            $match = [regex]::Match($_, '(?m)^\s*(?<path>(?:[A-Za-z]:\\|/)[^\r\n<>]+?)\s*$')
            if ($match.Success) {
                [System.IO.Path]::GetFullPath($match.Groups['path'].Value)
            }
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        if ($paths.Count -eq 0) {
            throw "Evaluated MSBuild preprocess query returned no import provenance for $($Evaluation.ProjectPath)."
        }
        $evaluatedImportCache[$cacheKey] = $paths
        return $paths
    }
    finally {
        Remove-Item $preprocessedPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-EvaluatedRepositoryImportClosure {
    param(
        [Parameter(Mandatory)]$Evaluation,
        [Parameter(Mandatory)][string]$BoundaryRoot
    )

    $normalizedBoundary = [System.IO.Path]::GetFullPath($BoundaryRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $boundaryPrefix = $normalizedBoundary + [System.IO.Path]::DirectorySeparatorChar
    $candidates = [System.Collections.Generic.List[string]]::new()
    $candidates.Add([string]$Evaluation.ProjectPath)
    foreach ($candidate in ([string]$Evaluation.Properties.MSBuildAllProjects).Split(
                 [char]';',
                 [StringSplitOptions]::RemoveEmptyEntries)) {
        $candidates.Add($candidate.Trim())
    }
    foreach ($candidate in @(Get-EvaluatedImportPaths -Evaluation $Evaluation)) {
        $candidates.Add($candidate)
    }
    foreach ($wellKnown in @('Directory.Build.props', 'Directory.Build.targets')) {
        $candidate = Join-Path $normalizedBoundary $wellKnown
        if (Test-Path $candidate -PathType Leaf) {
            $candidates.Add($candidate)
        }
    }

    return @($candidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
        Where-Object {
            ($_ -ceq $normalizedBoundary -or $_.StartsWith($boundaryPrefix, [StringComparison]::Ordinal)) -and
            $_ -notmatch '[\\/](?:bin|obj)[\\/]' -and
            (Test-Path $_ -PathType Leaf)
        } |
        Sort-Object -Unique)
}

function Assert-NoTargetTimeBuildGraphMutation {
    param(
        [Parameter(Mandatory)]$Evaluation,
        [Parameter(Mandatory)][string]$BoundaryRoot,
        [Parameter(Mandatory)][string]$Label
    )

    $imports = @(Get-EvaluatedRepositoryImportClosure -Evaluation $Evaluation -BoundaryRoot $BoundaryRoot)
    if ($imports.Count -eq 0) {
        throw "$Label has an empty evaluated repository import closure."
    }
    foreach ($import in $imports) {
        try {
            [xml]$document = Get-Content $import -Raw
        }
        catch {
            throw "$Label evaluated import is not valid MSBuild XML: file=$import error=$($_.Exception.Message)"
        }
        $dynamicNodes = @($document.SelectNodes($dynamicBuildGraphXPath))
        if ($dynamicNodes.Count -gt 0) {
            throw "$Label evaluated import mutates the compile/dependency graph at target execution time: file=$import nodes=$($dynamicNodes.Count)"
        }
    }
    return $imports
}

function Assert-CompatibleTestSupportClosure {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Runtime,
        [Parameter(Mandatory)][string]$Assembly
    )

    $forbiddenSupportPattern = switch ($Runtime) {
        'Pure' { '(?:IntegrationTestKit|FilesystemTestKit|CloudPlatform\.TestKit)' }
        'Filesystem' { '(?:IntegrationTestKit|CloudPlatform\.TestKit)' }
        'SQLite' { '(?:IntegrationTestKit|FilesystemTestKit)' }
        default { $null }
    }
    if ($null -eq $forbiddenSupportPattern) {
        return
    }

    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue([System.IO.Path]::GetFullPath($ProjectPath))
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        if (-not $visited.Add($current)) {
            continue
        }
        $currentEvaluation = Get-EvaluatedProject -ProjectPath $current
        foreach ($reference in @($currentEvaluation.References)) {
            if (-not (Test-Path $reference -PathType Leaf)) {
                throw "$Assembly reaches a missing evaluated ProjectReference: $reference"
            }
            if ([System.IO.Path]::GetFileName($reference) -match $forbiddenSupportPattern) {
                $relativeReference = [System.IO.Path]::GetRelativePath($repoRoot, $reference).Replace('\', '/')
                throw "$Runtime runner reaches incompatible test support transitively: assembly=$Assembly reference=$relativeReference"
            }
            $pending.Enqueue($reference)
        }
    }
}

function Test-EvaluatedProjectReachability {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$TargetProjectPath
    )

    $target = [System.IO.Path]::GetFullPath($TargetProjectPath)
    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue([System.IO.Path]::GetFullPath($ProjectPath))
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        if (-not $visited.Add($current)) {
            continue
        }
        if ([string]::Equals($current, $target, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        $evaluation = Get-EvaluatedProject -ProjectPath $current
        foreach ($reference in @($evaluation.References)) {
            if (-not (Test-Path $reference -PathType Leaf)) {
                throw "Evaluated project closure reaches a missing ProjectReference: $reference"
            }
            $pending.Enqueue($reference)
        }
    }
    return $false
}

function Test-EvaluatedProjectGraphQuery {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "cloud-evaluated-graph-$([Guid]::NewGuid().ToString('N'))"
    $closureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "cloud-evaluated-closure-$([Guid]::NewGuid().ToString('N'))"
    try {
        [void](New-Item $fixtureRoot -ItemType Directory -Force)
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'Directory.Build.props'),
            "<Project><Import Project=`"nested.props`" /></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'nested.props'),
            @'
<Project>
  <PropertyGroup Condition="'$(Configuration)' != 'Never' And 'Release' == 'Release'">
    <AssemblyName>Injected.Tests</AssemblyName>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' != 'Never' And 'Release' == 'Release'">
    <ProjectReference Include="Target.csproj" />
    <Compile Include="Shared.cs" Link="Linked/Shared.cs" />
  </ItemGroup>
  <ItemGroup Condition="'$(Configuration)' == 'InjectedPackage'">
    <PackageReference Include="Moq" Version="4.20.72" />
  </ItemGroup>
</Project>
'@,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'hidden.xml'),
            '<Project><Target Name="Hidden"><ItemGroup><Compile Include="hidden.cs" /></ItemGroup></Target></Project>',
            [System.Text.UTF8Encoding]::new($false))
        $nestedPropsPath = Join-Path $fixtureRoot 'nested.props'
        $nestedProps = Get-Content $nestedPropsPath -Raw
        $nestedProps = $nestedProps.Replace('<Project>', '<Project><Import Project="hidden.xml" />')
        [System.IO.File]::WriteAllText($nestedPropsPath, $nestedProps, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'Runner.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'Target.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'MultiTarget.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks></PropertyGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot 'Shared.cs'),
            "internal sealed class SharedFixture { }`n",
            [System.Text.UTF8Encoding]::new($false))

        $runnerPath = Join-Path $fixtureRoot 'Runner.csproj'
        $targetPath = [System.IO.Path]::GetFullPath((Join-Path $fixtureRoot 'Target.csproj'))
        $active = Get-EvaluatedProject -ProjectPath $runnerPath -ProjectConfiguration 'Release'
        $inactive = Get-EvaluatedProject -ProjectPath $runnerPath -ProjectConfiguration 'Never'
        if (@($active.References).Count -ne 1 -or
            [string]$active.References[0] -cne $targetPath -or
            @($inactive.References).Count -ne 0 -or
            [string]$active.Properties.AssemblyName -cne 'Injected.Tests' -or
            [string]$active.Properties.IsTestProject -cne 'true' -or
            @($active.Compiles | Where-Object Link -eq 'Linked/Shared.cs').Count -ne 1) {
            throw "Evaluated graph fixture failed: active=$(@($active.References).Count) inactive=$(@($inactive.References).Count)."
        }
        $compileFailure = $null
        try {
            Assert-ProjectCompileOwnership -Evaluation $active -Label 'Fixture.Runner'
        }
        catch {
            $compileFailure = $_.Exception.Message
        }
        if ($compileFailure -notmatch 'compiles linked or cross-project source') {
            throw "Evaluated Compile Link fixture was not rejected: $compileFailure"
        }
        $importClosureFailure = $null
        try {
            Assert-NoTargetTimeBuildGraphMutation `
                -Evaluation $active `
                -BoundaryRoot $fixtureRoot `
                -Label 'Fixture.HiddenImport' | Out-Null
        }
        catch {
            $importClosureFailure = $_.Exception.Message
        }
        if ($importClosureFailure -notmatch 'hidden\.xml' -or
            $importClosureFailure -notmatch 'target execution time') {
            throw "Arbitrary-extension evaluated import fixture was not rejected: $importClosureFailure"
        }
        $multiTargetFailure = $null
        try {
            Get-EvaluatedProject -ProjectPath (Join-Path $fixtureRoot 'MultiTarget.csproj') | Out-Null
        }
        catch {
            $multiTargetFailure = $_.Exception.Message
        }
        if ($multiTargetFailure -notmatch 'Multi-target project evaluation is unsupported') {
            throw "Evaluated multi-target fixture was not rejected: $multiTargetFailure"
        }
        $packageFailure = $null
        try {
            Assert-ProductionPackageBoundary `
                -Evaluation (Get-EvaluatedProject -ProjectPath $runnerPath -ProjectConfiguration 'InjectedPackage') `
                -Label 'Fixture.ImportedPackage'
        }
        catch {
            $packageFailure = $_.Exception.Message
        }
        if ($packageFailure -notmatch 'imports test-only packages through the evaluated graph') {
            throw "Imported test package fixture was not rejected: $packageFailure"
        }
        $disabledAnalyzerFailure = $null
        try {
            Assert-ProductionAnalyzerEnforcement -Label 'Fixture.DisabledAnalyzer' -Evaluation ([pscustomobject]@{
                Properties = [pscustomobject]@{
                    RunAnalyzers = 'false'
                    RunAnalyzersDuringBuild = 'false'
                    NoWarn = 'CLOUDARCH009'
                }
                ProjectReferences = @()
            })
        }
        catch {
            $disabledAnalyzerFailure = $_.Exception.Message
        }
        if ($disabledAnalyzerFailure -notmatch 'disables required architecture analyzer execution') {
            throw "Disabled Analyzer enforcement fixture was not rejected: $disabledAnalyzerFailure"
        }

        [void](New-Item $closureRoot -ItemType Directory -Force)
        [System.IO.File]::WriteAllText(
            (Join-Path $closureRoot 'PureRunner.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=`"Support.csproj`" /></ItemGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $closureRoot 'Support.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=`"IIoT.CloudPlatform.IntegrationTestKit.csproj`" /></ItemGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $closureRoot 'IIoT.CloudPlatform.IntegrationTestKit.csproj'),
            "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>`n",
            [System.Text.UTF8Encoding]::new($false))
        $closureFailure = $null
        try {
            Assert-CompatibleTestSupportClosure `
                -ProjectPath (Join-Path $closureRoot 'PureRunner.csproj') `
                -Runtime 'Pure' `
                -Assembly 'Fixture.PureRunner'
        }
        catch {
            $closureFailure = $_.Exception.Message
        }
        if ($closureFailure -notmatch 'reaches incompatible test support transitively') {
            throw "Evaluated transitive-support fixture was not rejected: $closureFailure"
        }
        if (-not (Test-EvaluatedProjectReachability `
                -ProjectPath (Join-Path $closureRoot 'PureRunner.csproj') `
                -TargetProjectPath (Join-Path $closureRoot 'IIoT.CloudPlatform.IntegrationTestKit.csproj')) -or
            (Test-EvaluatedProjectReachability `
                -ProjectPath (Join-Path $closureRoot 'PureRunner.csproj') `
                -TargetProjectPath (Join-Path $fixtureRoot 'Target.csproj'))) {
            throw 'Evaluated project reachability fixture did not distinguish used and unused support.'
        }
    }
    finally {
        Remove-Item $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $closureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-EvaluatedProjectGraphQuery

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

function Get-EffectiveMetadata($runner, $source, [string]$propertyName) {
    if ($null -ne $source.PSObject.Properties[$propertyName]) {
        return $source.$propertyName
    }
    if ($null -ne $runner.PSObject.Properties[$propertyName]) {
        return $runner.$propertyName
    }
    throw "Missing inherited metadata '$propertyName' for $($runner.assembly)/$($source.class)."
}

$allowedTestKinds = @('Architecture', 'Unit', 'Aggregate', 'Application', 'Contract', 'Conformance', 'Persistence', 'Workflow', 'Integration', 'EndToEnd', 'UI', 'GoldenEval', 'Deployment', 'Performance', 'SoakChaos')
$allowedRuntimes = @('Pure', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'Avalonia', 'Browser', 'Windows', 'LiveExternal')
$allowedRisks = @('P0', 'P1', 'P2')
$allowedConcerns = @('Security', 'Reliability', 'Compatibility', 'Accessibility', 'Performance')
$allowedCadences = @('PR', 'Nightly', 'Release', 'Manual')
$allowedProfiles = @('Default', 'Simulation', 'GoldenDataset', 'LiveExternal', 'WorkspaceAlignment')
$allowedDependencyTokens = @($allowedRuntimes + 'AICopilotWorkspace')
$requiredMetadata = @('testKind', 'capability', 'runtime', 'risk', 'concern', 'cadence', 'profile', 'regressionId', 'ruleId', 'owner')
$sourceOverrideProperties = @('capability', 'risk', 'concern', 'regressionId', 'ruleId', 'runtimeDependencies')

$regressionBaselines = @($manifest.regressionBaselines)
if ($regressionBaselines.Count -eq 0 -or
    @($regressionBaselines | Group-Object { [string]$_.regressionId } | Where-Object Count -ne 1).Count -gt 0) {
    throw 'Cloud test inventory must declare unique regressionBaselines.'
}
foreach ($baseline in $regressionBaselines) {
    if ([string]::IsNullOrWhiteSpace([string]$baseline.regressionId) -or [int]$baseline.minimumCases -le 0) {
        throw 'Every regression baseline must declare a non-blank regressionId and a positive minimumCases floor.'
    }
}
foreach ($runner in $manifest.runners) {
    foreach ($propertyName in $requiredMetadata) {
        if ($null -eq $runner.PSObject.Properties[$propertyName]) {
            throw "Runner $($runner.assembly) is missing metadata '$propertyName'."
        }
        if ([string]::IsNullOrWhiteSpace([string]$runner.$propertyName)) {
            throw "Runner $($runner.assembly) has blank metadata '$propertyName'."
        }
    }
    if ($allowedTestKinds -notcontains [string]$runner.testKind) {
        throw "Runner $($runner.assembly) has unsupported testKind '$($runner.testKind)'."
    }
    if ($allowedRuntimes -notcontains [string]$runner.runtime) {
        throw "Runner $($runner.assembly) has unsupported runtime '$($runner.runtime)'."
    }
    if ($allowedRisks -notcontains [string]$runner.risk -or
        $allowedConcerns -notcontains [string]$runner.concern -or
        $allowedCadences -notcontains [string]$runner.cadence -or
        $allowedProfiles -notcontains [string]$runner.profile) {
        throw "Runner $($runner.assembly) has an unsupported risk/concern/cadence/profile value."
    }
    if ($null -eq $runner.PSObject.Properties['runtimeDependencies']) {
        throw "Runner $($runner.assembly) must declare runtimeDependencies explicitly."
    }

    foreach ($source in $runner.sources) {
        foreach ($property in $source.PSObject.Properties) {
            if ($property.Name -in @('class', 'file', 'additionalFiles', 'expected')) {
                continue
            }
            if ($sourceOverrideProperties -notcontains $property.Name) {
                throw "Source $($runner.assembly)/$($source.class) uses forbidden metadata override '$($property.Name)'."
            }
        }
        foreach ($propertyName in $requiredMetadata) {
            $value = Get-EffectiveMetadata $runner $source $propertyName
            if ([string]::IsNullOrWhiteSpace([string]$value)) {
                throw "Source $($runner.assembly)/$($source.class) resolves blank metadata '$propertyName'."
            }
        }
        $effectiveRisk = [string](Get-EffectiveMetadata $runner $source 'risk')
        $effectiveConcern = [string](Get-EffectiveMetadata $runner $source 'concern')
        if ($allowedRisks -notcontains $effectiveRisk -or $allowedConcerns -notcontains $effectiveConcern) {
            throw "Source $($runner.assembly)/$($source.class) resolves unsupported risk/concern metadata."
        }
        $regressionId = Get-EffectiveMetadata $runner $source 'regressionId'
        $ruleId = [string](Get-EffectiveMetadata $runner $source 'ruleId')
        if (-not [string]::IsNullOrWhiteSpace([string]$regressionId) -and [string]::IsNullOrWhiteSpace($ruleId)) {
            throw "Regression-tagged source $($runner.assembly)/$($source.class) must resolve a RuleId."
        }
        if ([string]$regressionId -eq 'CLOUD-CACHE-001' -and $ruleId -ne 'CLOUD-CACHE-001') {
            throw "CLOUD-CACHE-001 source must resolve RuleId=CLOUD-CACHE-001: $($runner.assembly)/$($source.class)."
        }
    }
}

$duplicateRunnerAssemblies = @($manifest.runners | Group-Object { [string]$_.assembly } | Where-Object Count -ne 1)
$duplicateRunnerProjects = @($manifest.runners | Group-Object { [string]$_.project } | Where-Object Count -ne 1)
$duplicateSupportAssemblies = @($manifest.supportAllowlist | Group-Object { [string]$_.assembly } | Where-Object Count -ne 1)
$duplicateSupportProjects = @($manifest.supportAllowlist | Group-Object { [string]$_.project } | Where-Object Count -ne 1)
if ($duplicateRunnerAssemblies.Count -gt 0 -or $duplicateRunnerProjects.Count -gt 0 -or
    $duplicateSupportAssemblies.Count -gt 0 -or $duplicateSupportProjects.Count -gt 0) {
    throw 'Runner/support manifest contains a duplicate assembly or project identity.'
}

$runnerProjects = @($manifest.runners | ForEach-Object { [string]$_.project } | Sort-Object)
$actualTestProjects = @(Get-ChildItem (Join-Path $repoRoot 'src/tests') -Filter '*.csproj' -Recurse |
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
if (@($manifest.runners | Where-Object {
    $supportProjects -contains [string]$_.project -or
    @($manifest.supportAllowlist.assembly) -contains [string]$_.assembly
}).Count -gt 0) {
    throw 'A runner identity overlaps the support allowlist.'
}

$allProjectPaths = @(Get-ChildItem $repoRoot -Filter '*.csproj' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](?:\.git|artifacts|bin|obj|node_modules)[\\/]' } |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$classifiedProjectPaths = @(
    @($manifest.runners | ForEach-Object { [string]$_.project })
    $supportProjects
    @(Get-ChildItem (Join-Path $repoRoot 'src') -Filter '*.csproj' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/]src[\\/](tests|testing)[\\/]' } |
        ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') })
) | Sort-Object -Unique
if (($allProjectPaths -join "`n") -cne ($classifiedProjectPaths -join "`n")) {
    throw "Repository project classification mismatch. Every csproj must be exactly one runner, support, or production project.`nall=$($allProjectPaths -join ',')`nclassified=$($classifiedProjectPaths -join ',')"
}
[xml]$solutionXml = Get-Content (Join-Path $repoRoot 'IIoT.CloudPlatform.slnx') -Raw
$solutionProjectPaths = @($solutionXml.SelectNodes('//Project') |
    ForEach-Object { ([string]$_.Path).Replace('\', '/') } |
    Sort-Object -Unique)
if (($allProjectPaths -join "`n") -cne ($solutionProjectPaths -join "`n")) {
    throw "Solution project membership mismatch. Every classified csproj must be built by IIoT.CloudPlatform.slnx.`nall=$($allProjectPaths -join ',')`nsolution=$($solutionProjectPaths -join ',')"
}

$runtimeExecutionGroups = @{
    Pure = 'Pure'
    Filesystem = 'Filesystem'
    SQLite = 'Database'
    Postgres = 'Database'
    Redis = 'Container'
    Aspire = 'Aspire'
}
foreach ($runner in $manifest.runners) {
    $projectPath = Resolve-RepoPath ([string]$runner.project)
    [xml]$projectXml = Get-Content $projectPath -Raw
    $projectEvaluation = Get-EvaluatedProject -ProjectPath $projectPath
    $declaredAssemblyNameNodes = @($projectXml.SelectNodes('/Project/PropertyGroup/AssemblyName'))
    if ($declaredAssemblyNameNodes.Count -gt 1) {
        throw "Test project declares multiple AssemblyName values: $($runner.project)"
    }
    $actualAssembly = if ($declaredAssemblyNameNodes.Count -eq 1) {
        ([string]$declaredAssemblyNameNodes[0].InnerText).Trim()
    } else {
        [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    }
    if ($actualAssembly -ne [string]$runner.assembly) {
        throw "Runner assembly mismatch: manifest=$($runner.assembly) project=$actualAssembly"
    }
    if ([string]$projectEvaluation.Properties.IsTestProject -ne 'true' -or
        [string]$projectEvaluation.Properties.AssemblyName -cne [string]$runner.assembly -or
        [string]$projectEvaluation.Properties.MSBuildProjectName -cne [System.IO.Path]::GetFileNameWithoutExtension($projectPath)) {
        throw "Runner evaluated identity mismatch: manifest=$($runner.assembly) project=$($runner.project) evaluatedAssembly=$($projectEvaluation.Properties.AssemblyName) evaluatedName=$($projectEvaluation.Properties.MSBuildProjectName) isTest=$($projectEvaluation.Properties.IsTestProject)"
    }
    Assert-ProjectCompileOwnership -Evaluation $projectEvaluation -Label "Runner $($runner.assembly)"
    Assert-NoTargetTimeBuildGraphMutation `
        -Evaluation $projectEvaluation `
        -BoundaryRoot $repoRoot `
        -Label "Runner $($runner.assembly)" | Out-Null

    $metadata = @{
        CloudTestKind = [string]$runner.testKind
        CloudTestRuntime = [string]$runner.runtime
        CloudTestExecutionGroup = [string]$runner.executionGroup
        CloudTestRequired = ([bool]$runner.required).ToString().ToLowerInvariant()
    }
    foreach ($entry in $metadata.GetEnumerator()) {
        $actual = Get-DirectProjectProperty $projectXml ([string]$entry.Key) ([string]$runner.project)
        if (-not [string]::Equals($actual, [string]$entry.Value, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runner metadata mismatch for $($runner.assembly)/$($entry.Key): manifest=$($entry.Value) project=$actual"
        }
        $evaluated = [string]$projectEvaluation.Properties.([string]$entry.Key)
        if (-not [string]::Equals($evaluated, [string]$entry.Value, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runner evaluated metadata mismatch for $($runner.assembly)/$($entry.Key): manifest=$($entry.Value) evaluated=$evaluated"
        }
    }

    $runtime = [string]$runner.runtime
    $expectedExecutionGroup = if ($runtime -eq 'Aspire' -and [string]$runner.profile -eq 'WorkspaceAlignment') {
        'Workspace'
    } elseif ($runtimeExecutionGroups.ContainsKey($runtime)) {
        [string]$runtimeExecutionGroups[$runtime]
    } else {
        $null
    }
    if ($null -eq $expectedExecutionGroup -or [string]$runner.executionGroup -ne $expectedExecutionGroup) {
        throw "Runner runtime/executionGroup mismatch for $($runner.assembly): runtime=$runtime group=$($runner.executionGroup)"
    }

    $runtimeDependencies = @($runner.runtimeDependencies | ForEach-Object { [string]$_ })
    if (@($runtimeDependencies | Group-Object | Where-Object Count -ne 1).Count -gt 0 -or
        @($runtimeDependencies | Where-Object { $allowedDependencyTokens -notcontains $_ }).Count -gt 0) {
        throw "Runner $($runner.assembly) has duplicate or unsupported runtimeDependencies."
    }
    switch ($runtime) {
        'Pure' {
            if ($runtimeDependencies.Count -ne 0) { throw "Pure runner must have no runtimeDependencies: $($runner.assembly)" }
        }
        'Filesystem' {
            if (($runtimeDependencies -join ',') -ne 'Filesystem') { throw "Filesystem runner dependencies must be exactly Filesystem: $($runner.assembly)" }
        }
        'SQLite' {
            if (($runtimeDependencies -join ',') -ne 'SQLite') { throw "SQLite runner dependencies must be exactly SQLite: $($runner.assembly)" }
        }
        'Postgres' {
            if ($runtimeDependencies -notcontains 'Postgres' -or $runtimeDependencies -notcontains 'Aspire' -or
                @($runtimeDependencies | Where-Object { $_ -notin @('Postgres', 'Aspire', 'Filesystem') }).Count -gt 0) {
                throw "Postgres runner must depend on Postgres+Aspire (optional Filesystem): $($runner.assembly)"
            }
        }
        'Redis' {
            if (($runtimeDependencies | Sort-Object) -join ',' -ne 'Docker,Redis') {
                throw "Redis runner dependencies must be exactly Redis+Docker: $($runner.assembly)"
            }
        }
        'Aspire' {
            if ($runtimeDependencies -notcontains 'Aspire') { throw "Aspire runner must include Aspire dependency: $($runner.assembly)" }
            if ([string]$runner.profile -eq 'WorkspaceAlignment') {
                if (($runtimeDependencies -join ',') -ne 'Aspire,AICopilotWorkspace') {
                    throw "WorkspaceAlignment dependencies must be Aspire+AICopilotWorkspace: $($runner.assembly)"
                }
            } elseif ($runtimeDependencies -contains 'AICopilotWorkspace') {
                throw "AICopilotWorkspace dependency is restricted to WorkspaceAlignment profile: $($runner.assembly)"
            }
        }
    }

    $projectReferencePaths = @($projectEvaluation.References)
    $projectReferenceNames = @($projectReferencePaths | ForEach-Object { [System.IO.Path]::GetFileName($_) })
    $runnerProjectReferences = @($projectReferencePaths | Where-Object {
        [System.IO.Path]::GetRelativePath($repoRoot, $_).Replace('\', '/') -match '^src/tests/'
    })
    if ($runnerProjectReferences.Count -gt 0) {
        throw "Runner references another runner through the evaluated graph: assembly=$($runner.assembly) refs=$($runnerProjectReferences -join ',')"
    }
    $forbiddenSupportPattern = switch ($runtime) {
        'Pure' { '(?:IntegrationTestKit|FilesystemTestKit|CloudPlatform\.TestKit)' }
        'Filesystem' { '(?:IntegrationTestKit|CloudPlatform\.TestKit)' }
        'SQLite' { '(?:IntegrationTestKit|FilesystemTestKit)' }
        default { $null }
    }
    if ($null -ne $forbiddenSupportPattern -and
        @($projectReferenceNames | Where-Object { $_ -match $forbiddenSupportPattern }).Count -gt 0) {
        throw "$runtime runner directly references incompatible test support: $($runner.assembly)"
    }
    if ($runtime -in @('Filesystem', 'SQLite')) {
        $runtimeLeaks = @(
            $projectReferenceNames |
                Where-Object { $_ -match '(?:IIoT\.AppHost|IntegrationTestKit)' }
            @($projectEvaluation.Packages) |
                Where-Object { $_ -match '^Aspire\.Hosting(?:\.Testing)?$' }
        )
        if ($runtimeLeaks.Count -gt 0) {
            throw "$runtime runner directly leaks Aspire/AppHost runtime dependencies: $($runner.assembly) refs=$($runtimeLeaks -join ',')"
        }
    }

    Assert-CompatibleTestSupportClosure `
        -ProjectPath $projectPath `
        -Runtime $runtime `
        -Assembly ([string]$runner.assembly)
}

foreach ($support in $manifest.supportAllowlist) {
    $supportProject = Resolve-RepoPath $support.project
    $supportEvaluation = Get-EvaluatedProject -ProjectPath $supportProject
    if ([string]$supportEvaluation.Properties.IsTestProject -eq 'true' -or
        [string]$supportEvaluation.Properties.AssemblyName -cne [string]$support.assembly -or
        [string]$supportEvaluation.Properties.MSBuildProjectName -cne [System.IO.Path]::GetFileNameWithoutExtension($supportProject)) {
        throw "Support project has an invalid evaluated role/identity: project=$($support.project) assembly=$($supportEvaluation.Properties.AssemblyName) name=$($supportEvaluation.Properties.MSBuildProjectName) isTest=$($supportEvaluation.Properties.IsTestProject)"
    }
    Assert-ProjectCompileOwnership -Evaluation $supportEvaluation -Label "Support $($support.assembly)"
    Assert-ProductionPackageBoundary -Evaluation $supportEvaluation -Label "Support $($support.assembly)"
    Assert-NoTargetTimeBuildGraphMutation `
        -Evaluation $supportEvaluation `
        -BoundaryRoot $repoRoot `
        -Label "Support $($support.assembly)" | Out-Null
    $supportRunnerReferences = @($supportEvaluation.References | Where-Object {
        [System.IO.Path]::GetRelativePath($repoRoot, $_).Replace('\', '/') -match '^src/tests/'
    })
    if ($supportRunnerReferences.Count -gt 0) {
        throw "Support project references a runner through the evaluated graph: project=$($support.project) refs=$($supportRunnerReferences -join ',')"
    }
    $supportDirectory = Split-Path $supportProject -Parent
    $supportCaseMatches = @(Get-ChildItem $supportDirectory -Filter '*.cs' -Recurse |
        Select-String -Pattern '\[(Fact|Theory)(Attribute)?(?:\(|\])' |
        Where-Object { $_.Path -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' })
    if ($supportCaseMatches.Count -gt 0) {
        throw "Support project contains test cases: $($support.project)"
    }
    $supportDiscoveryOutput = @(& dotnet test $supportProject `
        -c $Configuration `
        --no-build `
        --no-restore `
        --disable-build-servers `
        --nologo `
        -noAutoResponse `
        --list-tests 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        @($supportDiscoveryOutput | Where-Object {
            [string]$_ -match '^\s+(?:[A-Za-z_][A-Za-z0-9_`+]*\.)+[A-Za-z_][A-Za-z0-9_`+]*(?:\(.+\))?\s*$'
        }).Count -ne 0) {
        throw "Support project must execute real zero-case discovery: project=$($support.project) exit=$LASTEXITCODE output=$($supportDiscoveryOutput -join ' | ')"
    }
}

function Test-JavaScriptSpawnProjectConsumer {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$Argument,
        [Parameter(Mandatory)][string]$ProjectPath
    )

    $astGate = Join-Path $PSScriptRoot 'Assert-JavaScriptSpawnProjectConsumer.mjs'
    $typescriptModule = Join-Path $repoRoot 'src/ui/iiot-web/node_modules/typescript/lib/typescript.js'
    if (-not (Test-Path $astGate -PathType Leaf) -or
        -not (Test-Path $typescriptModule -PathType Leaf)) {
        throw "JavaScript AST support-consumer gate is unavailable: gate=$astGate typescript=$typescriptModule"
    }

    $output = @($Source | & node $astGate $typescriptModule $Command $Argument $ProjectPath 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        return $true
    }
    if ($exitCode -eq 1) {
        return $false
    }
    throw "JavaScript AST support-consumer analysis failed: exit=$exitCode output=$($output -join ' | ')"
}

$externalSpawnDecoy = @'
// spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
const message = "spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {})";
const template = `spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {})`;
'@
if (Test-JavaScriptSpawnProjectConsumer `
        -Source $externalSpawnDecoy `
        -Command 'dotnet' `
        -Argument '--project' `
        -ProjectPath 'src/testing/Decoy/Decoy.csproj') {
    throw 'External support consumer fixture accepted a comment/string literal without a real spawn argument.'
}

$externalSpawnNegativeFixtures = @(
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
{
  const spawn = () => undefined;
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
'@,
    @'
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
if (false) {
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
function neverCalled() {
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve as pathResolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = pathResolve(dirname(fileURLToPath(import.meta.url)), '..');
{
  const resolve = (...parts) => parts.join('/');
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = '/tmp/not-the-repository';
spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve('/tmp', '..');
spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
function noop(callback) { return callback; }
noop(() => spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {}));
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
function returnsBeforeSpawn() {
  return;
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
returnsBeforeSpawn();
'@,
    @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
function throwsBeforeSpawn() {
  throw new Error('stop');
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
}
throwsBeforeSpawn();
'@
)
foreach ($fixtureSource in $externalSpawnNegativeFixtures) {
    if (Test-JavaScriptSpawnProjectConsumer `
            -Source $fixtureSource `
            -Command 'dotnet' `
            -Argument '--project' `
            -ProjectPath 'src/testing/Decoy/Decoy.csproj') {
    throw 'JavaScript AST support-consumer gate accepted an unbound symbol, invalid root, callback decoy, or unreachable spawn fixture.'
    }
}

$externalSpawnPositiveFixture = @'
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
new Promise((ready) => {
  spawn('dotnet', ['--project', resolve(repoRoot, 'src/testing/Decoy/Decoy.csproj')], {});
  ready();
});
'@
if (-not (Test-JavaScriptSpawnProjectConsumer `
        -Source $externalSpawnPositiveFixture `
        -Command 'dotnet' `
        -Argument '--project' `
        -ProjectPath 'src/testing/Decoy/Decoy.csproj')) {
    throw 'JavaScript AST support-consumer gate rejected a symbol-bound reachable Promise executor.'
}

foreach ($support in $manifest.supportAllowlist) {
    $supportProject = Resolve-RepoPath ([string]$support.project)
    $consumers = @($manifest.runners | Where-Object {
        Test-EvaluatedProjectReachability `
            -ProjectPath (Resolve-RepoPath ([string]$_.project)) `
            -TargetProjectPath $supportProject
    } | ForEach-Object { [string]$_.assembly })
    $externalConsumers = @()
    if ($null -ne $support.PSObject.Properties['externalConsumers']) {
        $externalConsumers = @($support.externalConsumers)
    }
    foreach ($externalConsumer in $externalConsumers) {
        $consumerFile = [string]$externalConsumer.file
        $consumerCommand = [string]$externalConsumer.command
        $consumerArgument = [string]$externalConsumer.argument
        $consumerProjectPath = [string]$externalConsumer.projectPath
        if ($consumerFile -notmatch '^src/' -or
            [string]::IsNullOrWhiteSpace($consumerCommand) -or
            [string]::IsNullOrWhiteSpace($consumerArgument) -or
            [string]::IsNullOrWhiteSpace($consumerProjectPath) -or
            $consumerProjectPath -cne [string]$support.project) {
            throw "Support external consumer evidence is invalid: project=$($support.project) file=$consumerFile"
        }
        $consumerPath = Resolve-RepoPath $consumerFile
        if (-not (Test-Path $consumerPath -PathType Leaf)) {
            throw "Support external consumer file is missing: project=$($support.project) file=$consumerFile"
        }
        $consumerSource = Get-Content $consumerPath -Raw
        if (-not (Test-JavaScriptSpawnProjectConsumer `
                -Source $consumerSource `
                -Command $consumerCommand `
                -Argument $consumerArgument `
                -ProjectPath $consumerProjectPath)) {
            throw "Support external consumer is not a real spawn command/project argument: project=$($support.project) file=$consumerFile"
        }
    }
    if ($consumers.Count -eq 0 -and $externalConsumers.Count -eq 0) {
        throw "Support project has no evaluated runner consumer and must be physically deleted: project=$($support.project)"
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
$msbuildAnalyzerDisablePattern = '(?is)<RunAnalyzers(?:DuringBuild)?\s*>\s*false\s*</RunAnalyzers(?:DuringBuild)?>|<NoWarn\s*>[^<]*CLOUDARCH\d{3}[^<]*</NoWarn>'
# SuppressMessage attributes are rejected by type identity rather than by a literal
# diagnostic-id argument. This fails closed for qualified names, Attribute suffixes,
# aliases and const-composed CLOUDARCH ids.
$sourceAnalyzerSuppressionPattern = '(?im)^\s*#pragma\s+warning\s+disable[^\r\n]*CLOUDARCH\d{3}|\b(?:Unconditional)?SuppressMessage(?:Attribute)?\b'
$configAnalyzerSuppressionPattern = '(?im)^\s*dotnet_diagnostic\.CLOUDARCH\d{3}\.severity\s*='
$workflowAnalyzerDisablePattern = '(?im)RunAnalyzers(?:DuringBuild)?\s*(?:=|:)\s*false\b'
foreach ($fixture in @(
    [pscustomobject]@{ text = '<RunAnalyzers>false</RunAnalyzers>'; pattern = $msbuildAnalyzerDisablePattern; label = 'MSBuild RunAnalyzers' },
    [pscustomobject]@{ text = '<NoWarn>$(NoWarn);CLOUDARCH009</NoWarn>'; pattern = $msbuildAnalyzerDisablePattern; label = 'MSBuild NoWarn' },
    [pscustomobject]@{ text = '#pragma warning disable CLOUDARCH009'; pattern = $sourceAnalyzerSuppressionPattern; label = 'source pragma' },
    [pscustomobject]@{ text = '[SuppressMessage("Architecture", "CLOUDARCH009")]'; pattern = $sourceAnalyzerSuppressionPattern; label = 'source suppression attribute' },
    [pscustomobject]@{ text = '[global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Architecture", "CLOUDARCH009")]'; pattern = $sourceAnalyzerSuppressionPattern; label = 'fully-qualified suppression attribute' },
    [pscustomobject]@{ text = 'using Silence = global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute; const string ArchitectureId = "CLOUDARCH009"; [assembly: Silence("Architecture", ArchitectureId)]'; pattern = $sourceAnalyzerSuppressionPattern; label = 'aliased suppression attribute with const diagnostic id' },
    [pscustomobject]@{ text = 'dotnet_diagnostic.CLOUDARCH009.severity = none'; pattern = $configAnalyzerSuppressionPattern; label = 'AnalyzerConfig severity' },
    [pscustomobject]@{ text = '-property:RunAnalyzers=false'; pattern = $workflowAnalyzerDisablePattern; label = 'workflow property' }
)) {
    if ([string]$fixture.text -notmatch [string]$fixture.pattern) {
        throw "Architecture Analyzer suppression policy fixture was not rejected: $($fixture.label)"
    }
}
$msbuildSuppressionFiles = @(Get-ChildItem $repoRoot -File -Recurse -Include '*.csproj', '*.props', '*.targets' |
    Where-Object { $_.FullName -notmatch '[\\/](?:\.git|artifacts|bin|obj|node_modules)[\\/]' })
foreach ($file in $msbuildSuppressionFiles) {
    if ((Get-Content $file.FullName -Raw) -match $msbuildAnalyzerDisablePattern) {
        throw "MSBuild file suppresses required Cloud architecture diagnostics or analyzer execution: $($file.FullName)"
    }
}
[xml]$dynamicBuildGraphFixture = '<Project><Target Name="Hidden"><ItemGroup><Compile Include="../hidden.cs" /></ItemGroup></Target></Project>'
if (@($dynamicBuildGraphFixture.SelectNodes($dynamicBuildGraphXPath)).Count -ne 1) {
    throw 'Dynamic MSBuild graph mutation fixture was not rejected.'
}
foreach ($file in $msbuildSuppressionFiles) {
    [xml]$msbuildDocument = Get-Content $file.FullName -Raw
    $dynamicGraphNodes = @($msbuildDocument.SelectNodes($dynamicBuildGraphXPath))
    if ($dynamicGraphNodes.Count -gt 0) {
        throw "Repository MSBuild target mutates the compile/dependency graph at execution time and cannot be reconciled statically: file=$($file.FullName) nodes=$($dynamicGraphNodes.Count)"
    }
}
$sourceSuppressionFiles = @(Get-ChildItem (Join-Path $repoRoot 'src') -File -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })
foreach ($file in $sourceSuppressionFiles) {
    if ((Get-Content $file.FullName -Raw) -match $sourceAnalyzerSuppressionPattern) {
        throw "C# source suppresses a non-configurable Cloud architecture diagnostic: $($file.FullName)"
    }
}
$analyzerConfigFiles = @(Get-ChildItem $repoRoot -File -Recurse -Include '.editorconfig', '*.editorconfig', '.globalconfig', '*.globalconfig' |
    Where-Object { $_.FullName -notmatch '[\\/](?:\.git|artifacts|bin|obj|node_modules)[\\/]' })
foreach ($file in $analyzerConfigFiles) {
    if ((Get-Content $file.FullName -Raw) -match $configAnalyzerSuppressionPattern) {
        throw "AnalyzerConfig attempts to change a non-configurable Cloud architecture diagnostic severity: $($file.FullName)"
    }
}
$workflowFiles = @(Get-ChildItem (Join-Path $repoRoot '.github') -File -Recurse -Include '*.yml', '*.yaml' -ErrorAction SilentlyContinue)
foreach ($file in $workflowFiles) {
    if ((Get-Content $file.FullName -Raw) -match $workflowAnalyzerDisablePattern) {
        throw "Workflow disables required Cloud architecture analyzer execution: $($file.FullName)"
    }
}
$testAndSupportSourceFiles = @(
    Get-ChildItem (Join-Path $repoRoot 'src/tests') -Filter '*.cs' -File -Recurse
    Get-ChildItem (Join-Path $repoRoot 'src/testing') -Filter '*.cs' -File -Recurse
) | Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]|[\\/]obj[\\/]' }
$testSourceText = @($testAndSupportSourceFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
if ($testSourceText -match 'Skip\s*=|\[(Fact|Theory)\s*\([^\]]*Skip') {
    throw 'Cloud test source still contains a conditional or unconditional Skip.'
}
$syncOverAsyncPattern = '\.GetAwaiter\(\)\.GetResult\(\)|\bTask\.Wait(?:All|Any)?\s*\('
foreach ($invalidSyncOverAsyncFixture in @(
    'Task.CompletedTask.GetAwaiter().GetResult()',
    'Task.WaitAll(work)'
)) {
    if ($invalidSyncOverAsyncFixture -notmatch $syncOverAsyncPattern) {
        throw "Sync-over-async policy fixture was not rejected: $invalidSyncOverAsyncFixture"
    }
}
if ('response.Result' -match $syncOverAsyncPattern) {
    throw 'Sync-over-async policy rejected a non-Task Result property fixture.'
}
if ($testSourceText -match $syncOverAsyncPattern) {
    throw 'Cloud test source still contains sync-over-async blocking.'
}

$delayMatches = @($testAndSupportSourceFiles | Select-String -Pattern '\bTask\.Delay\s*\(')
$delayGroups = @($delayMatches | Group-Object {
    $relative = [System.IO.Path]::GetRelativePath($repoRoot, $_.Path).Replace('\', '/')
    return "$relative|$($_.Line.Trim())"
})
foreach ($group in $delayGroups) {
    $separator = $group.Name.IndexOf('|', [StringComparison]::Ordinal)
    $file = $group.Name.Substring(0, $separator)
    $pattern = $group.Name.Substring($separator + 1)
    $allow = @($manifest.boundedPollingAllowlist | Where-Object {
        [string]$_.file -eq $file -and [string]$_.pattern -eq $pattern
    })
    if ($allow.Count -ne 1 -or $group.Count -ne [int]$allow[0].expectedCount) {
        throw "Fixed Task.Delay is not exactly allowlisted: file=$file pattern='$pattern' count=$($group.Count)."
    }
}
foreach ($allow in $manifest.boundedPollingAllowlist) {
    if ([string]::IsNullOrWhiteSpace([string]$allow.reason) -or
        $allowedRuntimes -notcontains [string]$allow.runtime) {
        throw "Bounded polling allowlist entry requires a reason and canonical runtime: $($allow.file)."
    }
    $matches = @($delayGroups | Where-Object {
        $_.Name -eq "$([string]$allow.file)|$([string]$allow.pattern)"
    })
    if ($matches.Count -ne 1 -or $matches[0].Count -ne [int]$allow.expectedCount) {
        throw "Bounded polling allowlist entry is stale: $($allow.file) '$($allow.pattern)'."
    }
    $allowlistedSource = Get-Content (Resolve-RepoPath ([string]$allow.file)) -Raw
    if (-not $allowlistedSource.Contains([string]$allow.timeoutEvidence, [StringComparison]::Ordinal)) {
        throw "Bounded polling entry lacks its declared timeout evidence '$($allow.timeoutEvidence)': $($allow.file)."
    }
}

$productionProjects = @(Get-ChildItem (Join-Path $repoRoot 'src') -Filter '*.csproj' -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]src[\\/](tests|testing)[\\/]' })
$productionCoverageAssemblies = [System.Collections.Generic.List[object]]::new()
foreach ($project in $productionProjects) {
    $productionEvaluation = Get-EvaluatedProject -ProjectPath $project.FullName
    $expectedProjectName = [System.IO.Path]::GetFileNameWithoutExtension($project.FullName)
    if ([string]$productionEvaluation.Properties.IsTestProject -eq 'true' -or
        [string]$productionEvaluation.Properties.MSBuildProjectName -cne $expectedProjectName -or
        [string]$productionEvaluation.Properties.AssemblyName -cne $expectedProjectName -or
        $expectedProjectName -match '(?i)(Tests?|Testing|TestKit|Fakes?|Mocks?)$') {
        throw "Production project has a test-like or mismatched evaluated identity: project=$($project.FullName) assembly=$($productionEvaluation.Properties.AssemblyName) name=$($productionEvaluation.Properties.MSBuildProjectName) isTest=$($productionEvaluation.Properties.IsTestProject)"
    }
    Assert-ProjectCompileOwnership -Evaluation $productionEvaluation -Label "Production $expectedProjectName"
    Assert-NoTargetTimeBuildGraphMutation `
        -Evaluation $productionEvaluation `
        -BoundaryRoot $repoRoot `
        -Label "Production $expectedProjectName" | Out-Null
    Assert-ProductionPackageBoundary `
        -Evaluation $productionEvaluation `
        -Label "Production $expectedProjectName"
    if ($expectedProjectName -cne 'IIoT.CloudPlatform.Analyzers') {
        Assert-ProductionAnalyzerEnforcement `
            -Evaluation $productionEvaluation `
            -Label "Production $expectedProjectName"
    }
    $testReferences = @($productionEvaluation.References | Where-Object {
        $relativeReference = [System.IO.Path]::GetRelativePath($repoRoot, $_).Replace('\', '/')
        $relativeReference -match '^src/(tests|testing)/' -or
        [System.IO.Path]::GetFileName($_) -match '(?i)(Tests?|Testing|TestKit|Fakes?|Mocks?)\.csproj$'
    })
    if ($testReferences.Count -gt 0) {
        throw "Production project references tests/TestKit through the evaluated graph: project=$($project.FullName) refs=$($testReferences -join ',')"
    }

    $relativeProject = [System.IO.Path]::GetRelativePath($repoRoot, $project.FullName).Replace('\', '/')
    if ($relativeProject -match '^src/(core|hosts|infrastructure|services|shared)/') {
        $targetPath = [System.IO.Path]::GetFullPath([string]$productionEvaluation.Properties.TargetPath)
        $pdbPath = [System.IO.Path]::ChangeExtension($targetPath, '.pdb')
        if (-not (Test-Path $targetPath -PathType Leaf) -or -not (Test-Path $pdbPath -PathType Leaf) -or
            [string]$productionEvaluation.Properties.DebugType -cne 'portable') {
            throw "Production coverage requires a built assembly and portable PDB: project=$relativeProject target=$targetPath pdb=$pdbPath debugType=$($productionEvaluation.Properties.DebugType)"
        }
        $productionCoverageAssemblies.Add([ordered]@{
            project = $relativeProject
            assembly = [string]$productionEvaluation.Properties.AssemblyName
            targetFramework = [string]$productionEvaluation.Properties.TargetFramework
            assemblyPath = [System.IO.Path]::GetRelativePath($repoRoot, $targetPath).Replace('\', '/')
            pdbPath = [System.IO.Path]::GetRelativePath($repoRoot, $pdbPath).Replace('\', '/')
            assemblySha256 = (Get-FileHash $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            pdbSha256 = (Get-FileHash $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
}

function Assert-ProductionCoverageAssemblyReconciliation($expectedProjects, $assemblyEvidence) {
    $expected = @($expectedProjects | Sort-Object -Unique)
    $actual = @($assemblyEvidence | ForEach-Object { [string]$_.project } | Sort-Object -Unique)
    if ($expected.Count -ne $actual.Count -or ($expected -join "`n") -cne ($actual -join "`n")) {
        throw "Production coverage assembly evidence omitted an evaluated project: expected=$($expected -join ',') actual=$($actual -join ',')"
    }
}
$coverageProjectPaths = @($productionProjects |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName).Replace('\', '/') } |
    Where-Object { $_ -match '^src/(core|hosts|infrastructure|services|shared)/' })
Assert-ProductionCoverageAssemblyReconciliation $coverageProjectPaths $productionCoverageAssemblies
$coverageOmissionFixture = $null
try {
    Assert-ProductionCoverageAssemblyReconciliation @('src/core/A/A.csproj', 'src/hosts/B/B.csproj') @(
        [pscustomobject]@{ project = 'src/core/A/A.csproj' })
}
catch {
    $coverageOmissionFixture = $_.Exception.Message
}
if ($coverageOmissionFixture -notmatch 'omitted an evaluated project') {
    throw "Unloaded production-project coverage fixture did not fail closed: $coverageOmissionFixture"
}

function Get-RunnerProductionBinaryEvidence($runner) {
    $runnerEvaluation = Get-EvaluatedProject -ProjectPath (Resolve-RepoPath ([string]$runner.project))
    $runnerOutputDirectory = Split-Path ([System.IO.Path]::GetFullPath([string]$runnerEvaluation.Properties.TargetPath)) -Parent
    $evidence = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in @($productionCoverageAssemblies | Sort-Object assembly)) {
        $assembly = [string]$entry.assembly
        $runnerAssemblyPath = Join-Path $runnerOutputDirectory "$assembly.dll"
        if (-not (Test-Path $runnerAssemblyPath -PathType Leaf)) {
            continue
        }
        $runnerPdbPath = Join-Path $runnerOutputDirectory "$assembly.pdb"
        if (-not (Test-Path $runnerPdbPath -PathType Leaf)) {
            throw "Coverage runner contains a production assembly without its portable PDB: runner=$($runner.assembly) assembly=$assembly"
        }
        $assemblySha256 = (Get-FileHash $runnerAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $pdbSha256 = (Get-FileHash $runnerPdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($assemblySha256 -cne [string]$entry.assemblySha256 -or
            $pdbSha256 -cne [string]$entry.pdbSha256) {
            throw "Coverage runner contains stale production assembly/PDB before execution: runner=$($runner.assembly) assembly=$assembly"
        }
        $evidence.Add([ordered]@{
            assembly = $assembly
            assemblySha256 = $assemblySha256
            pdbSha256 = $pdbSha256
        })
    }
    return @($evidence)
}

$expectedTotal = [int](($manifest.runners | Measure-Object -Property expected -Sum).Sum)
$expectedRequired = [int](($manifest.runners | Where-Object required | Measure-Object -Property expected -Sum).Sum)
if ($expectedTotal -ne [int]$manifest.baselineCases -or $expectedRequired -ne [int]$manifest.requiredCases) {
    throw "Manifest totals are inconsistent: total=$expectedTotal required=$expectedRequired."
}

$caseInventory = [System.Collections.Generic.List[object]]::new()
$runnerEvidence = [System.Collections.Generic.List[object]]::new()
$discoveredCounts = @{}
$materialIoPattern = '\b(?:File|Directory)\s*\.|\b(?:FileStream|FileInfo|DirectoryInfo)\b|\bPath\s*\.\s*GetTempPath\b|\bFindRepoFile\s*\(|\bZip(?:File|Archive)\b'
foreach ($runner in $manifest.runners) {
    $duplicateSourceClasses = @($runner.sources | Group-Object { [string]$_.class } | Where-Object Count -ne 1)
    if ($duplicateSourceClasses.Count -gt 0) {
        throw "Runner $($runner.assembly) contains duplicate source class rules."
    }
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
            $resolvedSourceFile = Resolve-RepoPath $sourceFile
            if (-not (Test-Path $resolvedSourceFile -PathType Leaf)) {
                throw "Inventory source does not exist: $sourceFile"
            }
            if ([string]$runner.runtime -eq 'Pure' -and
                (Get-Content $resolvedSourceFile -Raw) -match $materialIoPattern) {
                throw "Pure runner contains material filesystem access and must be physically split: $($runner.assembly) source=$sourceFile token=$($Matches[0])"
            }
        }
        if ($null -ne $source.PSObject.Properties['runtime']) {
            throw "Source-level runtime overrides are forbidden; physically split the runner: $($runner.assembly)/$($source.class)"
        }
        $sourceDependencies = if ($null -ne $source.PSObject.Properties['runtimeDependencies']) {
            @($source.runtimeDependencies | ForEach-Object { [string]$_ })
        } else {
            @($runner.runtimeDependencies | ForEach-Object { [string]$_ })
        }
        if (@($sourceDependencies | Group-Object | Where-Object Count -ne 1).Count -gt 0 -or
            @($sourceDependencies | Where-Object { $allowedDependencyTokens -notcontains $_ }).Count -gt 0) {
            throw "Source has duplicate or unsupported runtimeDependencies: $($runner.assembly)/$($source.class)"
        }
        if ([string]$runner.runtime -eq 'Postgres') {
            if ($sourceDependencies -notcontains 'Postgres' -or $sourceDependencies -notcontains 'Aspire' -or
                @($sourceDependencies | Where-Object { $_ -notin @('Postgres', 'Aspire', 'Filesystem') }).Count -gt 0) {
                throw "Postgres source dependencies must be Postgres+Aspire with optional Filesystem: $($runner.assembly)/$($source.class)"
            }
        } elseif (($sourceDependencies -join ',') -ne (@($runner.runtimeDependencies | ForEach-Object { [string]$_ }) -join ',')) {
            throw "Only a Postgres source may add the Filesystem dependency: $($runner.assembly)/$($source.class)"
        }
    }

    $cases = @(Get-DiscoveredCases $runner)
    $duplicateDiscoveredCases = @($cases | Group-Object | Where-Object Count -ne 1)
    if ($duplicateDiscoveredCases.Count -gt 0) {
        throw "Discovery returned duplicate case identities for $($runner.assembly): $($duplicateDiscoveredCases.Name -join ', ')"
    }
    if ($cases.Count -ne [int]$runner.expected) {
        throw "Discovery count mismatch for $($runner.assembly): expected=$($runner.expected) discovered=$($cases.Count)"
    }
    $discoveredCounts[[string]$runner.assembly] = $cases.Count
    $runnerEvidence.Add([pscustomobject][ordered]@{
        assembly = [string]$runner.assembly
        selected = $false
        expected = [int]$runner.expected
        discovered = $cases.Count
        total = $null
        executed = $null
        passed = $null
        failed = $null
        notExecuted = $null
    })
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
            testKind = [string](Get-EffectiveMetadata $runner $source 'testKind')
            capability = [string](Get-EffectiveMetadata $runner $source 'capability')
            runtime = [string](Get-EffectiveMetadata $runner $source 'runtime')
            runtimeDependencies = if ($null -ne $source.PSObject.Properties['runtimeDependencies']) {
                @($source.runtimeDependencies)
            } else {
                @($runner.runtimeDependencies)
            }
            risk = [string](Get-EffectiveMetadata $runner $source 'risk')
            concern = [string](Get-EffectiveMetadata $runner $source 'concern')
            cadence = [string](Get-EffectiveMetadata $runner $source 'cadence')
            profile = [string](Get-EffectiveMetadata $runner $source 'profile')
            regressionId = Get-EffectiveMetadata $runner $source 'regressionId'
            ruleId = [string](Get-EffectiveMetadata $runner $source 'ruleId')
            owner = [string](Get-EffectiveMetadata $runner $source 'owner')
            required = [bool]$runner.required
            skip = $false
            recentStatus = 'NotScheduled'
        })
    }
}
if ($caseInventory.Count -ne [int]$manifest.baselineCases) {
    throw "Case inventory mismatch: expected=$($manifest.baselineCases) actual=$($caseInventory.Count)"
}
$duplicateInventoryCases = @($caseInventory | Group-Object { [string]$_.case } | Where-Object Count -ne 1)
if ($duplicateInventoryCases.Count -gt 0) {
    throw "Cross-runner discovery returned duplicate case identities: $($duplicateInventoryCases.Name -join ', ')"
}
foreach ($case in $caseInventory) {
    foreach ($propertyName in @('testKind', 'capability', 'runtime', 'risk', 'concern', 'cadence', 'profile', 'regressionId', 'ruleId', 'owner')) {
        if (-not $case.Contains($propertyName)) {
            throw "Case '$($case.case)' is missing materialized metadata '$propertyName'."
        }
        if ([string]::IsNullOrWhiteSpace([string]$case[$propertyName])) {
            throw "Case '$($case.case)' has blank materialized metadata '$propertyName'."
        }
    }
}

$actualRegressionGroups = @($caseInventory | Group-Object { [string]$_.regressionId })
$unknownRegressionGroups = @($actualRegressionGroups | Where-Object {
    $groupName = [string]$_.Name
    @($regressionBaselines | Where-Object regressionId -eq $groupName).Count -ne 1
})
if ($unknownRegressionGroups.Count -gt 0) {
    throw "Cases resolved unbaselined RegressionIds: $($unknownRegressionGroups.Name -join ', ')."
}
$regressionEvidence = [System.Collections.Generic.List[object]]::new()
foreach ($baseline in $regressionBaselines) {
    $regressionId = [string]$baseline.regressionId
    $groups = @($actualRegressionGroups | Where-Object Name -eq $regressionId)
    $actualCases = if ($groups.Count -eq 1) { [int]$groups[0].Count } else { 0 }
    if ($groups.Count -ne 1 -or $actualCases -lt [int]$baseline.minimumCases) {
        throw "RegressionId coverage fell below its non-zero floor: id=$regressionId minimum=$($baseline.minimumCases) actual=$actualCases."
    }
    $regressionEvidence.Add([ordered]@{
        regressionId = $regressionId
        minimumCases = [int]$baseline.minimumCases
        actualCases = $actualCases
        delta = $actualCases - [int]$baseline.minimumCases
    })
}
$regressionZeroCount = @($regressionEvidence | Where-Object { [int]$_['actualCases'] -le 0 }).Count
if ($regressionZeroCount -ne 0 -or
    [int](($regressionEvidence | ForEach-Object { [int]$_['actualCases'] } | Measure-Object -Sum).Sum) -ne $caseInventory.Count) {
    throw 'RegressionId evidence did not reconcile to the complete case inventory.'
}

$selectedRunners = @(switch ($Mode) {
    'Required' { @($manifest.runners | Where-Object required) }
    'EndToEnd' { @($manifest.runners | Where-Object assembly -eq 'IIoT.CloudPlatform.EndToEndTests') }
    'WorkspaceAlignment' { @($manifest.runners | Where-Object assembly -eq 'IIoT.CloudPlatform.WorkspaceAlignmentTests') }
    default { @() }
})
foreach ($evidence in $runnerEvidence) {
    $evidence.selected = @($selectedRunners | Where-Object assembly -eq $evidence.assembly).Count -eq 1
}

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
    $runnerResultsRoot = if ($CollectCoverage) {
        Join-Path $resultsRoot "coverage/raw/$($runner.assembly)"
    } else {
        $resultsRoot
    }
    if ($CollectCoverage) {
        Remove-Item $runnerResultsRoot -Force -Recurse -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $runnerResultsRoot -Force | Out-Null
    $productionBinaryEvidence = if ($CollectCoverage) {
        @(Get-RunnerProductionBinaryEvidence $runner)
    } else {
        @()
    }
    $trxName = "$($runner.assembly).trx"
    $trxPath = Join-Path $runnerResultsRoot $trxName
    Remove-Item $trxPath -Force -ErrorAction SilentlyContinue
    $arguments = @(
        'test', (Resolve-RepoPath $runner.project),
        '-c', $Configuration,
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse',
        '--logger', "trx;LogFileName=$trxName",
        '--results-directory', $runnerResultsRoot
    )
    if ($NoBuild) {
        $arguments += @('--no-build', '--no-restore')
    }
    if ($CollectCoverage) {
        $arguments += @(
            '--collect', 'XPlat Code Coverage',
            '--',
            'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura',
            'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude=[*Tests]*,[*TestKit]*,[IIoT.CloudPlatform.PortFakes]*,[IIoT.CloudPlatform.Analyzers]*'
        )
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
        ResultsRoot = $runnerResultsRoot
        Process = $process
        Stdout = $stdout
        Stderr = $stderr
        ProductionBinaryEvidence = $productionBinaryEvidence
    }
}

$coverageReports = [System.Collections.Generic.List[object]]::new()
function Get-CoberturaProductionAssemblies([string]$coveragePath) {
    [xml]$coverage = Get-Content $coveragePath -Raw
    $assemblies = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $packagesNode = $coverage.coverage.packages
    $packageNodes = if ($null -eq $packagesNode -or $null -eq $packagesNode.PSObject.Properties['package']) {
        @()
    } else {
        @($packagesNode.package)
    }
    foreach ($package in $packageNodes) {
        $packageName = [string]$package.name
        if ($packageName -notmatch '^IIoT\.' -or
            $packageName -match '(?i)(Tests?|Testing|TestKit|Fakes?|Mocks?|Analyzers)$') {
            continue
        }
        if ($null -eq $package.classes -or $null -eq $package.classes.PSObject.Properties['class']) {
            continue
        }
        $hasExecutableProductionSource = $false
        foreach ($class in @($package.classes.class)) {
            $filename = ([string]$class.filename).Replace('\', '/')
            if ($filename -match '^(core|hosts|infrastructure|services|shared)/' -and
                $filename -notmatch '(?i)/Migrations/' -and
                $null -ne $class.lines -and
                $null -ne $class.lines.PSObject.Properties['line'] -and
                @($class.lines.line).Count -gt 0) {
                $hasExecutableProductionSource = $true
                break
            }
        }
        if ($hasExecutableProductionSource) {
            $null = $assemblies.Add($packageName)
        }
    }
    return @($assemblies | Sort-Object)
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
    $discovered = [int]$discoveredCounts[[string]$runner.assembly]
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $notExecuted = [int]$counters.notExecuted
    if ($discovered -ne [int]$runner.expected -or
        $total -ne $discovered -or $executed -ne $discovered -or $passed -ne $discovered -or
        $failed -ne 0 -or $notExecuted -ne 0) {
        throw "$($runner.assembly) reconciliation failed: expected=$($runner.expected) discovered=$discovered total=$total executed=$executed passed=$passed failed=$failed notExecuted=$notExecuted"
    }
    foreach ($case in $caseInventory | Where-Object assembly -eq $runner.assembly) {
        $case.recentStatus = 'Passed'
    }
    $evidence = @($runnerEvidence | Where-Object assembly -eq $runner.assembly)
    if ($evidence.Count -ne 1) {
        throw "Runner evidence identity mismatch for $($runner.assembly)."
    }
    $evidence[0].total = $total
    $evidence[0].executed = $executed
    $evidence[0].passed = $passed
    $evidence[0].failed = $failed
    $evidence[0].notExecuted = $notExecuted
    if ($CollectCoverage) {
        $coverageFiles = @(Get-ChildItem $execution.ResultsRoot -Filter 'coverage.cobertura.xml' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]_[^\\/]+[\\/]In[\\/]' })
        if ($coverageFiles.Count -ne 1) {
            throw "Expected exactly one Cobertura report for $($runner.assembly), found $($coverageFiles.Count)."
        }
        $reportProductionAssemblies = @(Get-CoberturaProductionAssemblies $coverageFiles[0].FullName)
        $reportBinaryEvidence = @($execution.ProductionBinaryEvidence | Where-Object {
            [string]$_.assembly -in $reportProductionAssemblies
        })
        if ($reportBinaryEvidence.Count -ne $reportProductionAssemblies.Count -or
            (@($reportBinaryEvidence | ForEach-Object { [string]$_.assembly } | Sort-Object) -join "`n") -cne
            ($reportProductionAssemblies -join "`n")) {
            throw "Cobertura production packages do not have exact collection-time DLL/PDB evidence: runner=$($runner.assembly) packages=$($reportProductionAssemblies -join ',') evidence=$(@($reportBinaryEvidence | ForEach-Object { [string]$_.assembly }) -join ',')"
        }
        $coverageReports.Add([ordered]@{
            assembly = [string]$runner.assembly
            path = [System.IO.Path]::GetRelativePath($resultsRoot, $coverageFiles[0].FullName).Replace('\', '/')
            productionBinaryEvidence = $reportBinaryEvidence
        })
    }
    Write-Host "CLOUD_TEST_RUNNER_OK assembly=$($runner.assembly) discovered=$discovered total=$total executed=$executed passed=$passed failed=0 skipped=0"
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
foreach ($evidence in $runnerEvidence) {
    if ($evidence.selected) {
        if ($null -eq $evidence.total -or $null -eq $evidence.executed -or $null -eq $evidence.passed -or
            [int]$evidence.discovered -ne [int]$evidence.expected -or
            [int]$evidence.total -ne [int]$evidence.expected -or
            [int]$evidence.executed -ne [int]$evidence.expected -or
            [int]$evidence.passed -ne [int]$evidence.expected -or
            [int]$evidence.failed -ne 0 -or [int]$evidence.notExecuted -ne 0) {
            throw "Selected runner evidence did not reconcile: $($evidence.assembly)."
        }
    } elseif ($null -ne $evidence.total -or $null -ne $evidence.executed -or $null -ne $evidence.passed -or
        $null -ne $evidence.failed -or $null -ne $evidence.notExecuted) {
        throw "Unselected runner unexpectedly contains execution counters: $($evidence.assembly)."
    }
}

$inventoryOutput = Join-Path $resultsRoot 'cloud-test-inventory.json'
[ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    mode = $Mode
    baselineCases = [int]$manifest.baselineCases
    requiredCases = [int]$manifest.requiredCases
    regressionZeroCount = $regressionZeroCount
    regressionEvidence = $regressionEvidence
    support = @($manifest.supportAllowlist | ForEach-Object {
        [ordered]@{ assembly = $_.assembly; project = $_.project; caseCount = 0 }
    })
    runnerEvidence = $runnerEvidence
    cases = $caseInventory
} | ConvertTo-Json -Depth 20 | Set-Content $inventoryOutput -Encoding utf8

if ($CollectCoverage) {
    $coverageIndexPath = Join-Path $resultsRoot 'coverage/cloud-coverage-index.json'
    New-Item -ItemType Directory -Path (Split-Path $coverageIndexPath -Parent) -Force | Out-Null
    [ordered]@{
        schemaVersion = 3
        mode = $Mode
        expectedReports = $selectedRunners.Count
        reports = $coverageReports
        productionAssemblies = @($productionCoverageAssemblies | Sort-Object assembly)
    } | ConvertTo-Json -Depth 10 | Set-Content $coverageIndexPath -Encoding utf8
    Write-Host "CLOUD_COVERAGE_COLLECTION_OK runners=$($selectedRunners.Count) reports=$($coverageReports.Count) output=$coverageIndexPath"
}

$executed = if ($selectedRunners.Count -eq 0) {
    0
} else {
    [int](($selectedRunners | Measure-Object -Property expected -Sum).Sum)
}
Write-Host "CLOUD_TEST_INVENTORY_OK baseline=$($manifest.baselineCases) required=$($manifest.requiredCases) selected=$executed regressionIds=$($regressionEvidence.Count) regressionZero=$regressionZeroCount support=$(@($manifest.supportAllowlist).Count) skipped=0 output=$inventoryOutput"
