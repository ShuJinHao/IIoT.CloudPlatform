Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExpectedCloudArchitectureBuildTargets = @'
<Project>
  <PropertyGroup>
    <CloudArchitectureRepositoryRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)'))</CloudArchitectureRepositoryRoot>
    <CloudArchitectureProjectIdentity>$([MSBuild]::MakeRelative('$(CloudArchitectureRepositoryRoot)', '$(MSBuildProjectFullPath)'))</CloudArchitectureProjectIdentity>
  </PropertyGroup>

  <ItemGroup Condition="'$(_IsCloudProductionProject)' == 'true'">
    <CompilerVisibleProperty Include="CloudArchitectureProjectIdentity" />
  </ItemGroup>

  <PropertyGroup Condition="'$(_IsCloudProductionProject)' == 'true'">
    <RunAnalyzers>true</RunAnalyzers>
    <RunAnalyzersDuringBuild>true</RunAnalyzersDuringBuild>
  </PropertyGroup>

  <Target Name="WriteCloudArchitectureManagedProjectReferences"
          DependsOnTargets="FindReferenceAssembliesForReferences"
          BeforeTargets="CoreCompile"
          Condition="'$(_IsCloudProductionProject)' == 'true'">
    <PropertyGroup>
      <_CloudArchitectureManagedReferencesFile>$(IntermediateOutputPath)CloudArchitectureManagedProjectReferences.txt</_CloudArchitectureManagedReferencesFile>
      <_CloudArchitectureResolvedProjectReferencesFile>$(IntermediateOutputPath)CloudArchitectureResolvedProjectReferences.txt</_CloudArchitectureResolvedProjectReferencesFile>
    </PropertyGroup>
    <ItemGroup>
      <_CloudArchitectureManagedReference Include="@(ReferencePathWithRefAssemblies)"
                                          Condition="'%(ReferencePathWithRefAssemblies.MSBuildSourceProjectFile)' != ''">
        <StableProjectIdentity>$([MSBuild]::MakeRelative('$(CloudArchitectureRepositoryRoot)', '%(ReferencePathWithRefAssemblies.MSBuildSourceProjectFile)'))</StableProjectIdentity>
      </_CloudArchitectureManagedReference>
      <_CloudArchitectureResolvedProjectReference Include="@(_ResolvedProjectReferencePaths)"
                                                  Condition="'%(_ResolvedProjectReferencePaths.MSBuildSourceProjectFile)' != ''">
        <CompilerReferencePath Condition="'%(_ResolvedProjectReferencePaths.ReferenceAssembly)' != ''">%(_ResolvedProjectReferencePaths.ReferenceAssembly)</CompilerReferencePath>
        <CompilerReferencePath Condition="'%(_ResolvedProjectReferencePaths.ReferenceAssembly)' == ''">%(_ResolvedProjectReferencePaths.FullPath)</CompilerReferencePath>
        <StableProjectIdentity>$([MSBuild]::MakeRelative('$(CloudArchitectureRepositoryRoot)', '%(_ResolvedProjectReferencePaths.MSBuildSourceProjectFile)'))</StableProjectIdentity>
      </_CloudArchitectureResolvedProjectReference>
    </ItemGroup>
    <WriteLinesToFile File="$(_CloudArchitectureManagedReferencesFile)"
                      Lines="@(_CloudArchitectureManagedReference->'%(FullPath)&#x9;%(MSBuildSourceProjectFile)&#x9;%(StableProjectIdentity)')"
                      Overwrite="true"
                      WriteOnlyWhenDifferent="true" />
    <WriteLinesToFile File="$(_CloudArchitectureResolvedProjectReferencesFile)"
                      Lines="@(_CloudArchitectureResolvedProjectReference->'%(CompilerReferencePath)&#x9;%(MSBuildSourceProjectFile)&#x9;%(StableProjectIdentity)')"
                      Overwrite="true"
                      WriteOnlyWhenDifferent="true" />
    <ItemGroup>
      <AdditionalFiles Include="$(_CloudArchitectureManagedReferencesFile)" />
      <AdditionalFiles Include="$(_CloudArchitectureResolvedProjectReferencesFile)" />
    </ItemGroup>
  </Target>

</Project>
'@

function Read-CloudArchitectureBuildTargetsXml {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Text
    )

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.IgnoreComments = $false
    $settings.IgnoreWhitespace = $false

    $stringReader = [IO.StringReader]::new($Text)
    $reader = [Xml.XmlReader]::Create($stringReader, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.PreserveWhitespace = $true
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    } finally {
        $reader.Dispose()
        $stringReader.Dispose()
    }
}

function Get-CloudArchitectureSemanticChildElements {
    param([Parameter(Mandatory)][Xml.XmlElement]$Element)

    return @($Element.ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element
        })
}

function Get-CloudArchitectureSemanticText {
    param([Parameter(Mandatory)][Xml.XmlElement]$Element)

    $builder = [Text.StringBuilder]::new()
    foreach ($child in $Element.ChildNodes) {
        switch ($child.NodeType) {
            ([Xml.XmlNodeType]::Text) {
                [void]$builder.Append([string]$child.Value)
            }
            ([Xml.XmlNodeType]::CDATA) {
                [void]$builder.Append([string]$child.Value)
            }
            ([Xml.XmlNodeType]::Whitespace) {
                [void]$builder.Append([string]$child.Value)
            }
            ([Xml.XmlNodeType]::SignificantWhitespace) {
                [void]$builder.Append([string]$child.Value)
            }
            ([Xml.XmlNodeType]::Comment) {
                continue
            }
        }
    }
    return $builder.ToString()
}

function Get-CloudArchitectureComparableAttributes {
    param([Parameter(Mandatory)][Xml.XmlElement]$Element)

    return @($Element.Attributes | ForEach-Object {
            [pscustomobject]@{
                Key = "$($_.NamespaceURI)|$($_.LocalName)"
                Value = [string]$_.Value
            }
        } | Sort-Object Key)
}

function Compare-CloudArchitectureBuildTargetsElement {
    param(
        [Parameter(Mandatory)][Xml.XmlElement]$Expected,
        [Parameter(Mandatory)][Xml.XmlElement]$Actual,
        [Parameter(Mandatory)][string]$Path
    )

    if ($Actual.LocalName -cne $Expected.LocalName -or
        $Actual.NamespaceURI -cne $Expected.NamespaceURI) {
        return "$Path`:element"
    }

    $expectedAttributes = @(Get-CloudArchitectureComparableAttributes -Element $Expected)
    $actualAttributes = @(Get-CloudArchitectureComparableAttributes -Element $Actual)
    if ($actualAttributes.Count -ne $expectedAttributes.Count) {
        return "$Path`:attributes"
    }
    for ($index = 0; $index -lt $expectedAttributes.Count; $index++) {
        if ($actualAttributes[$index].Key -cne $expectedAttributes[$index].Key -or
            $actualAttributes[$index].Value -cne $expectedAttributes[$index].Value) {
            return "$Path`:attribute:$($expectedAttributes[$index].Key)"
        }
    }

    $expectedChildren = @(Get-CloudArchitectureSemanticChildElements -Element $Expected)
    $actualChildren = @(Get-CloudArchitectureSemanticChildElements -Element $Actual)
    if ($actualChildren.Count -ne $expectedChildren.Count) {
        return "$Path`:children"
    }

    foreach ($child in $Actual.ChildNodes) {
        if ($child.NodeType -notin @(
                [Xml.XmlNodeType]::Element,
                [Xml.XmlNodeType]::Text,
                [Xml.XmlNodeType]::CDATA,
                [Xml.XmlNodeType]::Whitespace,
                [Xml.XmlNodeType]::SignificantWhitespace,
                [Xml.XmlNodeType]::Comment)) {
            return "$Path`:node:$($child.NodeType)"
        }
    }

    if ($expectedChildren.Count -eq 0) {
        $expectedText = Get-CloudArchitectureSemanticText -Element $Expected
        $actualText = Get-CloudArchitectureSemanticText -Element $Actual
        if ($actualText -cne $expectedText) {
            return "$Path`:text"
        }
    } else {
        foreach ($child in $Actual.ChildNodes) {
            if ($child.NodeType -in @(
                    [Xml.XmlNodeType]::Text,
                    [Xml.XmlNodeType]::CDATA,
                    [Xml.XmlNodeType]::Whitespace,
                    [Xml.XmlNodeType]::SignificantWhitespace) -and
                -not [string]::IsNullOrWhiteSpace([string]$child.Value)) {
                return "$Path`:mixed-text"
            }
        }
    }

    for ($index = 0; $index -lt $expectedChildren.Count; $index++) {
        $childPath = "$Path/$($expectedChildren[$index].LocalName)[$($index + 1)]"
        $difference = Compare-CloudArchitectureBuildTargetsElement `
            -Expected $expectedChildren[$index] `
            -Actual $actualChildren[$index] `
            -Path $childPath
        if (-not [string]::IsNullOrWhiteSpace($difference)) {
            return $difference
        }
    }

    return ''
}

function New-CloudArchitectureBuildTargetsContractResult {
    param(
        [Parameter(Mandatory)][bool]$IsSafe,
        [Parameter(Mandatory)][string]$Reason,
        [Parameter(Mandatory)][string]$Path
    )

    return [pscustomobject][ordered]@{
        IsSafe = $IsSafe
        Reason = $Reason
        Path = $Path
    }
}

function Test-CloudArchitectureBuildTargetsContract {
    [CmdletBinding(DefaultParameterSetName = 'RepositoryRoot')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'RepositoryRoot')]
        [Alias('Root')]
        [string]$RepositoryRoot,

        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$Path,

        [switch]$PassThru
    )

    $resolvedPath = if ($PSCmdlet.ParameterSetName -eq 'RepositoryRoot') {
        [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'Directory.Build.targets'))
    } else {
        [IO.Path]::GetFullPath($Path)
    }

    $result = $null
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        $result = New-CloudArchitectureBuildTargetsContractResult `
            -IsSafe $false `
            -Reason 'missing-file' `
            -Path $resolvedPath
    } else {
        try {
            $actualText = Get-Content -LiteralPath $resolvedPath -Raw
            $expectedDocument = Read-CloudArchitectureBuildTargetsXml `
                -Text $script:ExpectedCloudArchitectureBuildTargets
            $actualDocument = Read-CloudArchitectureBuildTargetsXml -Text $actualText

            $unexpectedDocumentNodes = @($actualDocument.ChildNodes | Where-Object {
                    $_.NodeType -notin @(
                        [Xml.XmlNodeType]::XmlDeclaration,
                        [Xml.XmlNodeType]::Element,
                        [Xml.XmlNodeType]::Whitespace,
                        [Xml.XmlNodeType]::SignificantWhitespace,
                        [Xml.XmlNodeType]::Comment)
                })
            $actualRoots = @($actualDocument.ChildNodes | Where-Object {
                    $_.NodeType -eq [Xml.XmlNodeType]::Element
                })
            if ($unexpectedDocumentNodes.Count -ne 0 -or $actualRoots.Count -ne 1) {
                $result = New-CloudArchitectureBuildTargetsContractResult `
                    -IsSafe $false `
                    -Reason 'document-shape' `
                    -Path $resolvedPath
            } else {
                $difference = Compare-CloudArchitectureBuildTargetsElement `
                    -Expected $expectedDocument.DocumentElement `
                    -Actual $actualDocument.DocumentElement `
                    -Path '/Project'
                $result = New-CloudArchitectureBuildTargetsContractResult `
                    -IsSafe ([string]::IsNullOrWhiteSpace($difference)) `
                    -Reason $(if ([string]::IsNullOrWhiteSpace($difference)) {
                            'safe'
                        } else {
                            "semantic-mismatch:$difference"
                        }) `
                    -Path $resolvedPath
            }
        } catch {
            $result = New-CloudArchitectureBuildTargetsContractResult `
                -IsSafe $false `
                -Reason "invalid-xml:$($_.Exception.GetType().Name)" `
                -Path $resolvedPath
        }
    }

    if ($PassThru) {
        return $result
    }
    return [bool]$result.IsSafe
}

Export-ModuleMember -Function Test-CloudArchitectureBuildTargetsContract
