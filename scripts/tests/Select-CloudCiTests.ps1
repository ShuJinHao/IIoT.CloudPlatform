[CmdletBinding()]
param(
    [ValidateSet('Default', 'Deployment', 'Quality', 'CrossProject', 'Full')]
    [string]$Mode = 'Default',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$BaseRef,
    [string]$HeadRef = 'HEAD',
    [string[]]$ChangedFiles,
    [string]$OutputPath = 'artifacts/ci-selection.json',
    [string]$GitHubOutputPath = $env:GITHUB_OUTPUT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path.Replace('\', '/').Trim()
    while ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('../', [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($normalized)) {
        throw "Changed file must be a repository-relative path: '$Path'."
    }
    return $normalized
}

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    return [IO.Path]::GetRelativePath($Root, [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Get-DirectProjectProperty {
    param(
        [Parameter(Mandatory)][xml]$Project,
        [Parameter(Mandatory)][string]$Name
    )

    $nodes = @($Project.SelectNodes(
        "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']"))
    $values = @($nodes | ForEach-Object { ([string]$_.InnerText).Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($values.Count -eq 0) {
        return ''
    }
    return [string]$values[-1]
}

function Get-CiCategory {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$TestKind,
        [string]$ExplicitCategory
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitCategory)) {
        return $ExplicitCategory
    }
    if ($ProjectName -ceq 'IIoT.CloudPlatform.WorkspaceAlignmentTests') {
        return 'CrossProject'
    }
    switch ($TestKind) {
        'Architecture' { return 'Architecture' }
        'Deployment' { return 'DeploymentContract' }
        'EndToEnd' { return 'Quality' }
        default { return 'Business' }
    }
}

function Get-GitFileAtRef {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Ref,
        [Parameter(Mandatory)][string]$Path
    )

    $output = @(& git -C $Root show "${Ref}:$Path" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return (($output | ForEach-Object { $_.ToString() }) -join "`n")
}

function Get-SolutionProjectPaths {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Source
    )

    try {
        [xml]$solution = $Text
    }
    catch {
        throw "Cloud solution XML is invalid: source=$Source"
    }
    return @($solution.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { ConvertTo-RepositoryPath ([string]$_.GetAttribute('Path')) } |
        Sort-Object -Unique)
}

function Get-BaselineProjectDescriptor {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Ref,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][hashtable]$Cache
    )

    if ($Cache.ContainsKey($ProjectPath)) {
        return $Cache[$ProjectPath]
    }
    $text = Get-GitFileAtRef -Root $Root -Ref $Ref -Path $ProjectPath
    if ($null -eq $text) {
        $Cache[$ProjectPath] = $null
        return $null
    }
    try {
        [xml]$projectXml = $text
    }
    catch {
        throw "Baseline Cloud project XML is invalid: ref=$Ref project=$ProjectPath"
    }
    $name = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $isTest = (Get-DirectProjectProperty -Project $projectXml -Name 'IsTestProject') -ceq 'true'
    $fullPath = [IO.Path]::GetFullPath((Join-Path $Root $ProjectPath))
    $descriptor = [pscustomobject]@{
        Path = $ProjectPath
        Name = $name
        Directory = Split-Path $fullPath -Parent
        RelativeDirectory = (Split-Path $ProjectPath -Parent).Replace('\', '/')
        Xml = $projectXml
        IsTest = $isTest
        TestKind = Get-DirectProjectProperty -Project $projectXml -Name 'CloudTestKind'
        Runtime = Get-DirectProjectProperty -Project $projectXml -Name 'CloudTestRuntime'
        ExplicitCategory = Get-DirectProjectProperty -Project $projectXml -Name 'CloudCiCategory'
        Security = (Get-DirectProjectProperty -Project $projectXml -Name 'CloudCiSecurity') -ceq 'true'
        Category = ''
        References = @()
    }
    if ($descriptor.IsTest) {
        $descriptor.Category = Get-CiCategory `
            -ProjectName $descriptor.Name `
            -TestKind $descriptor.TestKind `
            -ExplicitCategory $descriptor.ExplicitCategory
    }
    $descriptor.References = @(Get-ProjectReferences -Root $Root -Project $descriptor)
    $Cache[$ProjectPath] = $descriptor
    return $descriptor
}

function Get-ProjectReferences {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][object]$Project
    )

    $references = [Collections.Generic.List[string]]::new()
    foreach ($node in @($Project.Xml.SelectNodes("//*[local-name()='ProjectReference']"))) {
        $include = ([string]$node.Include).Trim()
        if ([string]::IsNullOrWhiteSpace($include) -or $include.Contains('$(')) {
            continue
        }
        $resolved = [IO.Path]::GetFullPath((Join-Path $Project.Directory $include))
        if (-not $resolved.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
            $resolved -cne $Root) {
            throw "ProjectReference escapes the repository: project=$($Project.Path) include=$include"
        }
        if (Test-Path $resolved -PathType Leaf) {
            $references.Add((Get-RepositoryRelativePath -Root $Root -Path $resolved))
        }
    }
    return @($references | Sort-Object -Unique)
}

function Get-ReferenceClosure {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][hashtable]$ProjectsByPath
    )

    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($ProjectPath)
    while ($pending.Count -gt 0) {
        $candidate = $pending.Pop()
        if (-not $visited.Add($candidate) -or -not $ProjectsByPath.ContainsKey($candidate)) {
            continue
        }
        foreach ($reference in @($ProjectsByPath[$candidate].References)) {
            $pending.Push([string]$reference)
        }
    }
    return @($visited | Sort-Object)
}

function Add-SelectedProject {
    param(
        [Parameter(Mandatory)][hashtable]$Selected,
        [Parameter(Mandatory)][object]$Project,
        [Parameter(Mandatory)][string]$Reason,
        [string]$Category = $Project.Category,
        [string]$TestFilter = ''
    )

    if (-not $Selected.ContainsKey($Project.Path)) {
        $Selected[$Project.Path] = [ordered]@{
            path = $Project.Path
            projectName = $Project.Name
            runtime = $Project.Runtime
            categories = [Collections.Generic.List[string]]::new()
            testFilter = $TestFilter
            reasons = [Collections.Generic.List[string]]::new()
        }
    } elseif ([string]::IsNullOrWhiteSpace($TestFilter)) {
        $Selected[$Project.Path].testFilter = ''
    } elseif (-not [string]::IsNullOrWhiteSpace([string]$Selected[$Project.Path].testFilter) -and
        [string]$Selected[$Project.Path].testFilter -cne $TestFilter) {
        $Selected[$Project.Path].testFilter =
            "($($Selected[$Project.Path].testFilter))|($TestFilter)"
    }
    if (-not $Selected[$Project.Path].categories.Contains($Category)) {
        $Selected[$Project.Path].categories.Add($Category)
    }
    if (-not $Selected[$Project.Path].reasons.Contains($Reason)) {
        $Selected[$Project.Path].reasons.Add($Reason)
    }
}

function Get-BusinessReplacementProjects {
    param(
        [Parameter(Mandatory)][object]$BaselineProject,
        [Parameter(Mandatory)][object[]]$CurrentTestProjects,
        [Parameter(Mandatory)][hashtable]$CurrentProjectClosures
    )

    $businessProjects = @($CurrentTestProjects | Where-Object {
            $_.Category -ceq 'Business' -and -not $_.Security
        })
    $matches = [Collections.Generic.List[object]]::new()
    foreach ($project in @($businessProjects | Where-Object Name -ceq $BaselineProject.Name)) {
        $matches.Add($project)
    }

    if ($matches.Count -eq 0) {
        $ownedSourceReferences = @($BaselineProject.References | Where-Object {
                -not $_.StartsWith('src/tests/', [StringComparison]::Ordinal) -and
                -not $_.StartsWith('src/testing/', [StringComparison]::Ordinal)
            })
        foreach ($project in $businessProjects) {
            $closure = @($CurrentProjectClosures[$project.Path])
            if (@($ownedSourceReferences | Where-Object { $closure -contains $_ }).Count -gt 0) {
                $matches.Add($project)
            }
        }
    }

    return @($matches | Sort-Object Path -Unique)
}

function Add-BaselineBusinessImpact {
    param(
        [Parameter(Mandatory)][hashtable]$Selected,
        [Parameter(Mandatory)][object]$BaselineProject,
        [Parameter(Mandatory)][object[]]$CurrentTestProjects,
        [Parameter(Mandatory)][hashtable]$CurrentProjectClosures,
        [Parameter(Mandatory)][string]$Mode,
        [Parameter(Mandatory)][string]$Reason,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[string]]$DeferredFiles,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.HashSet[string]]$RetiredBusinessProjects
    )

    if (-not $BaselineProject.IsTest -or
        $BaselineProject.Category -cne 'Business' -or
        $BaselineProject.Security) {
        return $false
    }
    $ownedSourceReferences = @($BaselineProject.References | Where-Object {
            -not $_.StartsWith('src/tests/', [StringComparison]::Ordinal) -and
            -not $_.StartsWith('src/testing/', [StringComparison]::Ordinal)
        })
    if ($ownedSourceReferences.Count -eq 0) {
        return $false
    }

    [void]$RetiredBusinessProjects.Add($BaselineProject.Path)
    if ($Mode -eq 'Deployment') {
        $DeferredFiles.Add("Business:$Reason")
        return $true
    }

    foreach ($replacement in @(Get-BusinessReplacementProjects `
                -BaselineProject $BaselineProject `
                -CurrentTestProjects $CurrentTestProjects `
                -CurrentProjectClosures $CurrentProjectClosures)) {
        Add-SelectedProject -Selected $Selected -Project $replacement `
            -Category Business -Reason "affected-retired-test:$($BaselineProject.Path)"
    }
    return $true
}

$root = (Resolve-Path $RepositoryRoot).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path (Join-Path $root 'IIoT.CloudPlatform.slnx') -PathType Leaf)) {
    throw "Cloud repository root is invalid: $root"
}

if (-not $PSBoundParameters.ContainsKey('ChangedFiles')) {
    if ($Mode -eq 'Deployment') {
        throw 'Deployment CI selection requires an explicit ChangedFiles set from the exact-SHA deployment impact plan.'
    } elseif ($Mode -ne 'Default') {
        $ChangedFiles = @()
    } else {
        if ([string]::IsNullOrWhiteSpace($BaseRef) -or $BaseRef -match '^0+$') {
            throw 'Default CI selection requires a non-zero BaseRef. Use workflow_dispatch mode Full for an initial branch history.'
        }
        $diffOutput = @(& git -C $root diff --no-renames --name-only --diff-filter=ACMRTUXBD "$BaseRef...$HeadRef" 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to calculate changed files for $BaseRef...${HeadRef}:`n$($diffOutput -join [Environment]::NewLine)"
        }
        $ChangedFiles = @($diffOutput)
    }
}
$changed = @($ChangedFiles |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    ForEach-Object { ConvertTo-RepositoryPath ([string]$_) } |
    Sort-Object -Unique)

$allProjects = [Collections.Generic.List[object]]::new()
foreach ($projectFile in @(Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' -File -Recurse |
        Sort-Object FullName)) {
    [xml]$projectXml = Get-Content $projectFile.FullName -Raw
    $path = Get-RepositoryRelativePath -Root $root -Path $projectFile.FullName
    $allProjects.Add([pscustomobject]@{
        Path = $path
        Name = [IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
        Directory = $projectFile.DirectoryName
        RelativeDirectory = (Split-Path $path -Parent).Replace('\', '/')
        Xml = $projectXml
        IsTest = (Get-DirectProjectProperty -Project $projectXml -Name 'IsTestProject') -ceq 'true'
        TestKind = Get-DirectProjectProperty -Project $projectXml -Name 'CloudTestKind'
        Runtime = Get-DirectProjectProperty -Project $projectXml -Name 'CloudTestRuntime'
        ExplicitCategory = Get-DirectProjectProperty -Project $projectXml -Name 'CloudCiCategory'
        Security = (Get-DirectProjectProperty -Project $projectXml -Name 'CloudCiSecurity') -ceq 'true'
        Category = ''
        References = @()
    })
}

$projectsByPath = @{}
foreach ($project in $allProjects) {
    if ($project.IsTest) {
        $project.Category = Get-CiCategory `
            -ProjectName $project.Name `
            -TestKind $project.TestKind `
            -ExplicitCategory $project.ExplicitCategory
        if ($project.Category -notin @(
                'Architecture', 'Security', 'Business', 'DeploymentContract', 'Quality', 'CrossProject')) {
            throw "Cloud test project has an invalid CI category: project=$($project.Path) category=$($project.Category)"
        }
    }
    $projectsByPath[$project.Path] = $project
}
foreach ($project in $allProjects) {
    $project.References = @(Get-ProjectReferences -Root $root -Project $project)
}

$testProjects = @($allProjects | Where-Object IsTest | Sort-Object Path)
$solutionPath = 'IIoT.CloudPlatform.slnx'
$baselineReady = $false
$baselineProjectPaths = @()
$baselineProjectDirectories = @()
$baselineProjectCache = @{}
if (-not [string]::IsNullOrWhiteSpace($BaseRef)) {
    & git -C $root cat-file -e "${BaseRef}^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $baselineSolutionText = Get-GitFileAtRef -Root $root -Ref $BaseRef -Path $solutionPath
        if ($null -ne $baselineSolutionText) {
            $baselineProjectPaths = @(Get-SolutionProjectPaths `
                -Text $baselineSolutionText `
                -Source "${BaseRef}:$solutionPath")
            $baselineProjectDirectories = @($baselineProjectPaths | ForEach-Object {
                    [pscustomobject]@{
                        Path = $_
                        RelativeDirectory = (Split-Path $_ -Parent).Replace('\', '/')
                    }
                } | Sort-Object @{ Expression = { $_.RelativeDirectory.Length }; Descending = $true })
            $baselineReady = $true
        }
    }
}
$selected = @{}
foreach ($project in $testProjects) {
    if ($Mode -ne 'Deployment' -and $project.Category -ceq 'Architecture') {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Architecture -Reason 'mandatory-architecture'
    }
    if ($Mode -ne 'Deployment' -and $project.Security) {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Security -Reason 'mandatory-security'
    }
}

$webRoot = 'src/ui/iiot-web/'
$webChanged = [Collections.Generic.List[string]]::new()
$webAffected = $false
$webFull = $Mode -in @('Quality', 'Full')
$deploymentAffected = $false
$unclassified = [Collections.Generic.List[string]]::new()
$deferredFiles = [Collections.Generic.List[string]]::new()
$retiredBusinessFiles = [Collections.Generic.List[string]]::new()
$retiredBusinessProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$requiredExplicitMode = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

if ($Mode -eq 'Quality') {
    foreach ($project in @($testProjects | Where-Object Category -ceq 'Quality')) {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category Quality -Reason 'manual-quality'
    }
} elseif ($Mode -eq 'Full') {
    foreach ($project in @($testProjects | Where-Object Category -cne 'CrossProject')) {
        Add-SelectedProject -Selected $selected -Project $project `
            -Category $project.Category -Reason 'manual-full'
    }
    $deploymentAffected = $true
} elseif ($Mode -eq 'CrossProject') {
    $selected.Clear()
    $workspaceProject = @($testProjects | Where-Object Category -ceq 'CrossProject')
    if ($workspaceProject.Count -ne 1) {
        throw 'CrossProject mode requires exactly one Cloud CrossProject test project.'
    }
    Add-SelectedProject -Selected $selected -Project $workspaceProject[0] `
        -Category CrossProject -Reason 'manual-cross-project'
} else {
    $projectClosures = @{}
    foreach ($testProject in $testProjects) {
        $projectClosures[$testProject.Path] = @(Get-ReferenceClosure `
            -ProjectPath $testProject.Path `
            -ProjectsByPath $projectsByPath)
    }

    $projectDirectories = @($allProjects |
        Sort-Object @{ Expression = { $_.RelativeDirectory.Length }; Descending = $true })
    foreach ($file in $changed) {
        if ($file.StartsWith($webRoot, [StringComparison]::Ordinal)) {
            if ($Mode -eq 'Deployment') {
                continue
            }
            $webRelative = $file.Substring($webRoot.Length)
            if ($webRelative -match '^(?:package(?:-lock)?\.json|vite\.config\.|vitest\.config\.|tsconfig|playwright\.|e2e/)') {
                $unclassified.Add($file)
                [void]$requiredExplicitMode.Add('Full')
            } else {
                $webAffected = $true
                $webChanged.Add($file)
            }
            continue
        }

        if ($file -match '^(?:docs/|AGENTS\.md$|README(?:\.[^/]+)?$|LICENSE(?:\.[^/]+)?$)') {
            continue
        }
        if ($file -match '^(?:\.github/workflows/|scripts/tests/|src/tests/[^/]+\.json$)') {
            continue
        }
        if ($file -ceq $solutionPath) {
            if (-not $baselineReady) {
                $unclassified.Add($file)
                [void]$requiredExplicitMode.Add('Full')
                continue
            }
            $currentSolutionPaths = @(Get-SolutionProjectPaths `
                -Text (Get-Content (Join-Path $root $solutionPath) -Raw) `
                -Source $solutionPath)
            $baselineSet = [Collections.Generic.HashSet[string]]::new(
                [string[]]$baselineProjectPaths,
                [StringComparer]::Ordinal)
            $currentSet = [Collections.Generic.HashSet[string]]::new(
                [string[]]$currentSolutionPaths,
                [StringComparer]::Ordinal)
            $solutionDelta = @(@(
                    @($baselineProjectPaths | Where-Object { -not $currentSet.Contains($_) })
                    @($currentSolutionPaths | Where-Object { -not $baselineSet.Contains($_) })
                ) | Sort-Object -Unique)
            $businessOnly = $solutionDelta.Count -gt 0
            foreach ($projectPath in $solutionDelta) {
                $project = if ($projectsByPath.ContainsKey($projectPath)) {
                    $projectsByPath[$projectPath]
                } else {
                    Get-BaselineProjectDescriptor `
                        -Root $root `
                        -Ref $BaseRef `
                        -ProjectPath $projectPath `
                        -Cache $baselineProjectCache
                }
                if ($null -eq $project -or -not $project.IsTest -or
                    $project.Category -cne 'Business' -or $project.Security) {
                    $businessOnly = $false
                    break
                }
            }
            if (-not $businessOnly) {
                $unclassified.Add($file)
                [void]$requiredExplicitMode.Add('Full')
                continue
            }
            $solutionSafelyAttributed = $true
            foreach ($projectPath in $solutionDelta) {
                if ($projectsByPath.ContainsKey($projectPath)) {
                    $project = $projectsByPath[$projectPath]
                    if ($Mode -eq 'Deployment') {
                        $deferredFiles.Add("Business:$file")
                    } else {
                        Add-SelectedProject -Selected $selected -Project $project `
                            -Category Business -Reason "affected-solution:$file"
                    }
                } else {
                    $baselineProject = Get-BaselineProjectDescriptor `
                        -Root $root `
                        -Ref $BaseRef `
                        -ProjectPath $projectPath `
                        -Cache $baselineProjectCache
                    if (-not (Add-BaselineBusinessImpact `
                                -Selected $selected `
                                -BaselineProject $baselineProject `
                                -CurrentTestProjects $testProjects `
                                -CurrentProjectClosures $projectClosures `
                                -Mode $Mode `
                                -Reason $projectPath `
                                -DeferredFiles $deferredFiles `
                                -RetiredBusinessProjects $retiredBusinessProjects)) {
                        $solutionSafelyAttributed = $false
                    }
                }
            }
            if (-not $solutionSafelyAttributed) {
                $unclassified.Add($file)
                [void]$requiredExplicitMode.Add('Full')
            }
            continue
        }
        if ($file -match '^(?:global\.json$|Directory\.(?:Build|Packages)\.(?:props|targets)$)') {
            $unclassified.Add($file)
            [void]$requiredExplicitMode.Add('Full')
            continue
        }
        if ($file.StartsWith('deploy/', [StringComparison]::Ordinal) -or
            ($file.StartsWith('scripts/', [StringComparison]::Ordinal) -and
                -not $file.StartsWith('scripts/tests/', [StringComparison]::Ordinal))) {
            $deploymentAffected = $true
            $deploymentProject = @($testProjects | Where-Object Name -eq 'IIoT.CloudPlatform.DeploymentTests')
            if ($deploymentProject.Count -ne 1) {
                $unclassified.Add($file)
            } else {
                Add-SelectedProject -Selected $selected -Project $deploymentProject[0] `
                    -Category DeploymentContract -Reason "affected:$file"
            }
            continue
        }

        $owner = @($projectDirectories | Where-Object {
                $file -ceq $_.Path -or
                $file.StartsWith("$($_.RelativeDirectory)/", [StringComparison]::Ordinal)
            } | Select-Object -First 1)
        if ($owner.Count -eq 1) {
            if ($owner[0].IsTest) {
                switch ($owner[0].Category) {
                    'Architecture' {
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category Architecture -Reason "affected-test:$file"
                    }
                    'Security' {
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category Security -Reason "affected-test:$file"
                    }
                    'Business' {
                        if ($Mode -eq 'Deployment') {
                            $deferredFiles.Add("Business:$file")
                        } else {
                            Add-SelectedProject -Selected $selected -Project $owner[0] `
                                -Category Business -Reason "affected-test:$file"
                        }
                    }
                    'DeploymentContract' {
                        $deploymentAffected = $true
                        Add-SelectedProject -Selected $selected -Project $owner[0] `
                            -Category DeploymentContract -Reason "affected-test:$file"
                    }
                    'Quality' {
                        $deferredFiles.Add("Quality:$file")
                        [void]$requiredExplicitMode.Add('Quality')
                    }
                    'CrossProject' {
                        $deferredFiles.Add("CrossProject:$file")
                        [void]$requiredExplicitMode.Add('CrossProject')
                    }
                }
                continue
            }

            $dependents = @($testProjects | Where-Object {
                    $projectClosures[$_.Path] -contains $owner[0].Path
                })
            $businessDependents = @($dependents | Where-Object Category -ceq 'Business')
            $mandatoryDependents = @($dependents | Where-Object {
                    $_.Category -ceq 'Architecture' -or $_.Security
                })
            if ($Mode -eq 'Deployment') {
                foreach ($dependent in @($mandatoryDependents | Where-Object {
                            $_.Category -ceq 'Architecture'
                        })) {
                    Add-SelectedProject -Selected $selected -Project $dependent `
                        -Category Architecture -Reason "affected-architecture:$($owner[0].Path)"
                }
                foreach ($dependent in @($mandatoryDependents | Where-Object Security)) {
                    Add-SelectedProject -Selected $selected -Project $dependent `
                        -Category Security -Reason "affected-security:$($owner[0].Path)"
                }
                continue
            }
            if ($businessDependents.Count -eq 0 -and $mandatoryDependents.Count -eq 0) {
                $qualityOnly = @($dependents | Where-Object Category -ceq 'Quality').Count -gt 0
                $crossOnly = @($dependents | Where-Object Category -ceq 'CrossProject').Count -gt 0
                if ($qualityOnly) {
                    $deferredFiles.Add("Quality:$file")
                    [void]$requiredExplicitMode.Add('Quality')
                }
                if ($crossOnly) {
                    $deferredFiles.Add("CrossProject:$file")
                    [void]$requiredExplicitMode.Add('CrossProject')
                }
                if (-not $qualityOnly -and -not $crossOnly) {
                    $unclassified.Add($file)
                }
                continue
            }
            foreach ($dependent in $businessDependents) {
                Add-SelectedProject -Selected $selected -Project $dependent `
                    -Category Business -Reason "affected:$($owner[0].Path)"
            }
            continue
        }

        if ($baselineReady) {
            $baselineOwner = @($baselineProjectDirectories | Where-Object {
                    $file -ceq $_.Path -or
                    $file.StartsWith("$($_.RelativeDirectory)/", [StringComparison]::Ordinal)
                } | Select-Object -First 1)
            if ($baselineOwner.Count -eq 1) {
                $baselineProject = Get-BaselineProjectDescriptor `
                    -Root $root `
                    -Ref $BaseRef `
                    -ProjectPath $baselineOwner[0].Path `
                    -Cache $baselineProjectCache
                if ($null -ne $baselineProject -and $baselineProject.IsTest -and
                    $baselineProject.Category -ceq 'Business' -and
                    -not $baselineProject.Security) {
                    if (Add-BaselineBusinessImpact `
                            -Selected $selected `
                            -BaselineProject $baselineProject `
                            -CurrentTestProjects $testProjects `
                            -CurrentProjectClosures $projectClosures `
                            -Mode $Mode `
                            -Reason $file `
                            -DeferredFiles $deferredFiles `
                            -RetiredBusinessProjects $retiredBusinessProjects) {
                        $retiredBusinessFiles.Add($file)
                        continue
                    }
                }
            }
        }

        if ($file.StartsWith('src/', [StringComparison]::Ordinal)) {
            $unclassified.Add($file)
            continue
        }
        $unclassified.Add($file)
    }
}

if ($Mode -in @('Quality', 'Full')) {
    $webAffected = $true
}

$selectedProjects = @($selected.Values | Sort-Object path | ForEach-Object {
        [ordered]@{
            path = [string]$_.path
            projectName = [string]$_.projectName
            runtime = [string]$_.runtime
            categories = @($_.categories | Sort-Object)
            testFilter = [string]$_.testFilter
            reasons = @($_.reasons | Sort-Object)
        }
    })
$requiresDocker = @($selectedProjects | Where-Object {
        $_.runtime -in @('Aspire', 'Postgres', 'Redis', 'RabbitMQ', 'Docker')
    }).Count -gt 0

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
}
[void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
$document = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    mode = $Mode
    baseRef = $BaseRef
    headRef = $HeadRef
    changedFiles = $changed
    discoveredTestProjects = $testProjects.Count
    selectedDotNetProjects = $selectedProjects
    selectedCategories = @($selectedProjects |
        ForEach-Object { @($_.categories) } |
        Sort-Object -Unique)
    web = [ordered]@{
        affected = $webAffected
        full = $webFull
        changedFiles = @($webChanged | Sort-Object -Unique)
    }
    deploymentAffected = $deploymentAffected
    requiresDocker = $requiresDocker
    deferredExplicitFiles = @($deferredFiles | Sort-Object -Unique)
    retiredBusinessFiles = @($retiredBusinessFiles | Sort-Object -Unique)
    retiredBusinessProjects = @($retiredBusinessProjects | Sort-Object)
    unclassifiedFiles = @($unclassified | Sort-Object -Unique)
    requiredExplicitModes = @($requiredExplicitMode | Sort-Object)
}
$document | ConvertTo-Json -Depth 10 | Set-Content $resolvedOutput -Encoding utf8

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    @(
        "selection_path=$OutputPath"
        "mode=$($Mode.ToLowerInvariant())"
        "web_affected=$($webAffected.ToString().ToLowerInvariant())"
        "web_full=$($webFull.ToString().ToLowerInvariant())"
        "deployment_affected=$($deploymentAffected.ToString().ToLowerInvariant())"
        "requires_docker=$($requiresDocker.ToString().ToLowerInvariant())"
    ) | Add-Content $GitHubOutputPath -Encoding utf8
}

if ($unclassified.Count -gt 0) {
    $modes = if ($requiredExplicitMode.Count -gt 0) {
        @($requiredExplicitMode | Sort-Object) -join ','
    } else {
        'Full'
    }
    throw "Cloud CI cannot safely attribute these files; no full-suite fallback was used. Run an explicit workflow_dispatch mode ($modes) after review:`n$(@($unclassified | Sort-Object -Unique) -join "`n")"
}

Write-Host "CLOUD_CI_SELECTION_OK mode=$Mode tests=$($selectedProjects.Count) web=$webAffected deployment=$deploymentAffected output=$resolvedOutput"
