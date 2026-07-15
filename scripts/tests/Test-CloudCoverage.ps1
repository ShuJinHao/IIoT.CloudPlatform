[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$BaseRef = $env:CLOUD_QUALITY_BASE_REF
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
. (Join-Path $PSScriptRoot 'CloudQualityBaselineProtection.ps1')
$baseCommit = Resolve-CloudQualityBaseCommit -RepoRoot $repoRoot -BaseRef $BaseRef
$baselinePath = Join-Path $PSScriptRoot 'baselines/cloud-coverage.json'
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 100
if ([int]$baseline.schemaVersion -ne 3) {
    throw "Cloud coverage baseline schemaVersion must be 3, actual=$($baseline.schemaVersion)."
}
$baseBaseline = Get-CloudQualityBaseJson `
    -RepoRoot $repoRoot `
    -BaseCommit $baseCommit `
    -RelativePath 'scripts/tests/baselines/cloud-coverage.json'
if ([int]$baseBaseline.schemaVersion -lt 2 -or [int]$baseline.schemaVersion -lt [int]$baseBaseline.schemaVersion) {
    throw "Cloud coverage baseline schema cannot be downgraded: base=$($baseBaseline.schemaVersion) candidate=$($baseline.schemaVersion)."
}
Assert-CloudQualityAtLeast ([int]$baseline.requiredRunnerCount) ([int]$baseBaseline.requiredRunnerCount) 'coverage required runner count'
Assert-CloudQualityAtLeast ([double]$baseline.merged.lineRate) ([double]$baseBaseline.merged.lineRate) 'merged line-rate threshold'
Assert-CloudQualityAtLeast ([double]$baseline.merged.branchRate) ([double]$baseBaseline.merged.branchRate) 'merged branch-rate threshold'
Assert-CloudQualityAtLeast ([double]$baseline.newP0Code.minimumLineRate) ([double]$baseBaseline.newP0Code.minimumLineRate) 'new-P0 line-rate threshold'
Assert-CloudQualityAtLeast ([double]$baseline.newP0Code.minimumBranchRate) ([double]$baseBaseline.newP0Code.minimumBranchRate) 'new-P0 branch-rate threshold'
$candidateP0Roots = @($baseline.newP0Code.roots | ForEach-Object { [string]$_ })
$removedP0Roots = @($baseBaseline.newP0Code.roots | Where-Object { [string]$_ -notin $candidateP0Roots })
if ($removedP0Roots.Count -gt 0) {
    throw "Cloud coverage baseline removes existing P0 roots: $($removedP0Roots -join ', ')."
}
$resultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}
$indexPath = Join-Path $resultsRoot 'coverage/cloud-coverage-index.json'
if (-not (Test-Path $indexPath -PathType Leaf)) {
    throw "Missing coverage index: $indexPath"
}
$index = Get-Content $indexPath -Raw | ConvertFrom-Json -Depth 100
if ([int]$index.schemaVersion -ne 3) {
    throw "Cloud coverage index schemaVersion must be 3, actual=$($index.schemaVersion)."
}

$propsSource = Get-Content (Join-Path $repoRoot 'src/tests/Directory.Build.props') -Raw
if (-not $propsSource.Contains("Include=`"$($baseline.collector)`"", [StringComparison]::Ordinal) -or
    -not $propsSource.Contains("Version=`"$($baseline.version)`"", [StringComparison]::Ordinal)) {
    throw "Coverage baseline collector/version must match the current test project configuration: $($baseline.collector) $($baseline.version)."
}

$testManifest = Get-Content (Join-Path $repoRoot 'src/tests/cloud-test-inventory.json') -Raw | ConvertFrom-Json -Depth 100
$requiredAssemblies = @($testManifest.runners | Where-Object required | ForEach-Object { [string]$_.assembly } | Sort-Object)
$reportAssemblies = @($index.reports | ForEach-Object { [string]$_.assembly } | Sort-Object)
$evaluatedProductionAssemblies = @($index.productionAssemblies | ForEach-Object { [string]$_.assembly } | Sort-Object)
if ($requiredAssemblies.Count -ne [int]$baseline.requiredRunnerCount -or
    [int]$index.expectedReports -ne [int]$baseline.requiredRunnerCount -or
    $reportAssemblies.Count -ne [int]$baseline.requiredRunnerCount -or
    ($requiredAssemblies -join "`n") -ne ($reportAssemblies -join "`n")) {
    throw "Coverage aggregation must contain exactly the required $($baseline.requiredRunnerCount) runners."
}

function Get-CoverageReport([string]$reportPath) {
    [xml]$coverage = Get-Content $reportPath -Raw
    $map = @{}
    $observedProductionAssemblies = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
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
        $packageHasExecutableSource = $false
        foreach ($class in @($package.classes.class)) {
            $filename = ([string]$class.filename).Replace('\', '/')
            if ($filename -notmatch '^(core|hosts|infrastructure|services|shared)/' -or
                $filename -match '(?i)/Migrations/') {
                continue
            }
            if ($null -eq $class.lines -or $null -eq $class.lines.PSObject.Properties['line']) {
                continue
            }
            foreach ($line in @($class.lines.line)) {
                $packageHasExecutableSource = $true
                $number = [int]$line.number
                $key = "${filename}:$number"
                $hits = [int]$line.hits
                $branchValid = 0
                $branchCovered = 0
                if ([string]$line.branch -eq 'True' -and
                    [string]$line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                    $branchCovered = [int]$Matches[1]
                    $branchValid = [int]$Matches[2]
                }
                if (-not $map.ContainsKey($key)) {
                    $map[$key] = [ordered]@{
                        filename = $filename
                        number = $number
                        hits = $hits
                        branchValid = $branchValid
                        branchCovered = $branchCovered
                    }
                } else {
                    $existing = $map[$key]
                    $existing.hits = [Math]::Max([int]$existing.hits, $hits)
                    $existing.branchValid = [Math]::Max([int]$existing.branchValid, $branchValid)
                    $existing.branchCovered = [Math]::Max([int]$existing.branchCovered, $branchCovered)
                }
            }
        }
        if ($packageHasExecutableSource) {
            $null = $observedProductionAssemblies.Add($packageName)
        }
    }
    return [pscustomobject]@{
        Map = $map
        ProductionAssemblies = @($observedProductionAssemblies | Sort-Object)
    }
}

function Assert-ProductionAssemblyObservation($expectedAssemblies, $observedAssemblies) {
    $expected = @($expectedAssemblies | Sort-Object -Unique)
    $observed = @($observedAssemblies | Sort-Object -Unique)
    $missing = @($expected | Where-Object { $_ -notin $observed })
    $unexpected = @($observed | Where-Object { $_ -notin $expected })
    if ($expected.Count -eq 0 -or
        $expected.Count -ne @($expectedAssemblies).Count -or
        $observed.Count -ne @($observedAssemblies).Count -or
        $missing.Count -gt 0 -or
        $unexpected.Count -gt 0) {
        throw "Coverage production-assembly observation mismatch: expected=$($expected.Count) observed=$($observed.Count) missing=$($missing -join ',') unexpected=$($unexpected -join ',')."
    }
}

$assemblyOmissionFixture = $null
try {
    Assert-ProductionAssemblyObservation @('IIoT.A', 'IIoT.B') @('IIoT.A')
}
catch {
    $assemblyOmissionFixture = $_.Exception.Message
}
if ($assemblyOmissionFixture -notmatch 'missing=IIoT.B') {
    throw "Coverage production-assembly omission fixture did not fail closed: $assemblyOmissionFixture"
}

function Get-Metrics($map) {
    $linesValid = 0
    $linesCovered = 0
    $branchesValid = 0
    $branchesCovered = 0
    foreach ($line in $map.Values) {
        $linesValid++
        if ([int]$line.hits -gt 0) {
            $linesCovered++
        }
        $branchesValid += [int]$line.branchValid
        $branchesCovered += [int]$line.branchCovered
    }
    return [ordered]@{
        linesValid = $linesValid
        linesCovered = $linesCovered
        branchesValid = $branchesValid
        branchesCovered = $branchesCovered
        lineRate = if ($linesValid -eq 0) { 1.0 } else { [Math]::Round($linesCovered / $linesValid, 8) }
        branchRate = if ($branchesValid -eq 0) { 1.0 } else { [Math]::Round($branchesCovered / $branchesValid, 8) }
    }
}

$portablePdbSha1Guid = [Guid]'ff1816ec-aa5e-4d10-87f7-6f4963833460'
$portablePdbSha256Guid = [Guid]'8829d00f-11b8-4213-878b-770e8597ac16'

function Assert-PortablePdbDocumentChecksum(
    [string]$sourcePath,
    [Guid]$algorithmGuid,
    [byte[]]$expectedHash) {
    if (-not (Test-Path $sourcePath -PathType Leaf) -or $expectedHash.Count -eq 0) {
        throw "Portable PDB document checksum evidence is incomplete: $sourcePath"
    }
    $sourceBytes = [System.IO.File]::ReadAllBytes($sourcePath)
    $actualHash = if ($algorithmGuid -eq $portablePdbSha256Guid) {
        [System.Security.Cryptography.SHA256]::HashData($sourceBytes)
    } elseif ($algorithmGuid -eq $portablePdbSha1Guid) {
        [System.Security.Cryptography.SHA1]::HashData($sourceBytes)
    } else {
        throw "Portable PDB document uses an unsupported hash algorithm: path=$sourcePath algorithm=$algorithmGuid"
    }
    if ([Convert]::ToHexString($actualHash) -cne [Convert]::ToHexString($expectedHash)) {
        throw "Current production source does not match its built portable PDB document checksum: $sourcePath"
    }
}

function Get-PortablePdbUniverse($assemblyEntries) {
    $universe = @{}
    $documentPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $assemblyEvidence = [System.Collections.Generic.List[object]]::new()
    $canonicalAssemblies = [System.Collections.Generic.List[string]]::new()
    $documentChecksumCount = 0
    $documentChecksumFixture = $null
    foreach ($entry in @($assemblyEntries | Sort-Object assembly)) {
        foreach ($property in @('project', 'assembly', 'targetFramework', 'assemblyPath', 'pdbPath', 'assemblySha256', 'pdbSha256')) {
            if ($null -eq $entry.PSObject.Properties[$property] -or
                [string]::IsNullOrWhiteSpace([string]$entry.$property)) {
                throw "Production coverage assembly evidence is missing '$property'."
            }
        }
        if ([string]$entry.project -notmatch '^src/(core|hosts|infrastructure|services|shared)/.+\.csproj$' -or
            [string]$entry.assemblyPath -notmatch '^src/(core|hosts|infrastructure|services|shared)/.+\.dll$' -or
            [string]$entry.pdbPath -notmatch '^src/(core|hosts|infrastructure|services|shared)/.+\.pdb$') {
            throw "Production coverage assembly evidence escapes the authoritative source roots: $($entry.project)"
        }
        $assemblyPath = Join-Path $repoRoot (([string]$entry.assemblyPath) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        $pdbPath = Join-Path $repoRoot (([string]$entry.pdbPath) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $assemblyPath -PathType Leaf) -or -not (Test-Path $pdbPath -PathType Leaf)) {
            throw "Production assembly/PDB is missing: assembly=$assemblyPath pdb=$pdbPath"
        }
        $actualAssemblySha256 = (Get-FileHash $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $actualPdbSha256 = (Get-FileHash $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualAssemblySha256 -cne [string]$entry.assemblySha256 -or
            $actualPdbSha256 -cne [string]$entry.pdbSha256) {
            throw "Production assembly/PDB changed after coverage collection: assembly=$($entry.assembly)"
        }

        $assemblyLines = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $stream = [System.IO.File]::OpenRead($pdbPath)
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
            try {
                $reader = $provider.GetMetadataReader()
                foreach ($documentHandle in $reader.Documents) {
                    $document = $reader.GetDocument($documentHandle)
                    $documentPath = $reader.GetString($document.Name).Replace('\', '/')
                    $relativePath = if ([System.IO.Path]::IsPathRooted($documentPath)) {
                        [System.IO.Path]::GetRelativePath($repoRoot, $documentPath).Replace('\', '/')
                    } else {
                        $documentPath.TrimStart([char[]]@('.', '/'))
                    }
                    if ($relativePath -match '^src/(core|hosts|infrastructure|services|shared)/.+\.cs$' -and
                        $relativePath -notmatch '(?i)/(?:bin|obj)/') {
                        if ($document.HashAlgorithm.IsNil -or $document.Hash.IsNil) {
                            throw "Portable PDB document lacks source checksum metadata: $relativePath"
                        }
                        $algorithmGuid = $reader.GetGuid($document.HashAlgorithm)
                        $expectedDocumentHash = $reader.GetBlobBytes($document.Hash)
                        $sourcePath = Join-Path $repoRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
                        Assert-PortablePdbDocumentChecksum $sourcePath $algorithmGuid $expectedDocumentHash
                        $documentChecksumCount++
                        if ($null -eq $documentChecksumFixture) {
                            $documentChecksumFixture = [pscustomobject]@{
                                sourcePath = $sourcePath
                                algorithmGuid = $algorithmGuid
                                expectedHash = $expectedDocumentHash
                            }
                        }
                        if ($relativePath -notmatch '(?i)/Migrations/') {
                            $null = $documentPaths.Add($relativePath)
                        }
                    }
                }
                foreach ($methodHandle in $reader.MethodDebugInformation) {
                    $method = $reader.GetMethodDebugInformation($methodHandle)
                    foreach ($sequencePoint in $method.GetSequencePoints()) {
                        if ($sequencePoint.IsHidden) {
                            continue
                        }
                        $documentHandle = if (-not $sequencePoint.Document.IsNil) {
                            $sequencePoint.Document
                        } else {
                            $method.Document
                        }
                        if ($documentHandle.IsNil) {
                            throw "Portable PDB contains a visible sequence point without a document: $pdbPath"
                        }
                        $document = $reader.GetDocument($documentHandle)
                        $documentPath = $reader.GetString($document.Name).Replace('\', '/')
                        $relativePath = if ([System.IO.Path]::IsPathRooted($documentPath)) {
                            [System.IO.Path]::GetRelativePath($repoRoot, $documentPath).Replace('\', '/')
                        } else {
                            $documentPath.TrimStart([char[]]@('.', '/'))
                        }
                        if ($relativePath -notmatch '^src/(core|hosts|infrastructure|services|shared)/' -or
                            $relativePath -match '(?i)/Migrations/' -or
                            $relativePath -match '(?i)/(?:bin|obj)/') {
                            continue
                        }
                        $coveragePath = $relativePath.Substring(4)
                        $key = "${coveragePath}:$([int]$sequencePoint.StartLine)"
                        $null = $assemblyLines.Add($key)
                        if (-not $universe.ContainsKey($key)) {
                            $universe[$key] = [ordered]@{
                                filename = $coveragePath
                                number = [int]$sequencePoint.StartLine
                                hits = 0
                                branchValid = 0
                                branchCovered = 0
                            }
                        }
                    }
                }
            }
            finally {
                $provider.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        if ($assemblyLines.Count -eq 0) {
            throw "Production assembly has no authoritative in-scope portable-PDB sequence points: $($entry.assembly)"
        }
        $assemblyLineDigest = Get-TextSha256 (@($assemblyLines | Sort-Object) -join "`n")
        $assemblyEvidence.Add([ordered]@{
            project = [string]$entry.project
            assembly = [string]$entry.assembly
            targetFramework = [string]$entry.targetFramework
            sequencePointLines = $assemblyLines.Count
            sequencePointSha256 = $assemblyLineDigest
        })
        $canonicalAssemblies.Add("$([string]$entry.project)`n$([string]$entry.assembly)`n$([string]$entry.targetFramework)`n$assemblyLineDigest")
    }
    if ($assemblyEvidence.Count -eq 0) {
        throw 'Coverage index contains no evaluated production assemblies.'
    }
    return [ordered]@{
        map = $universe
        assemblies = $assemblyEvidence
        assemblyCount = $assemblyEvidence.Count
        assemblySha256 = Get-TextSha256 (@($canonicalAssemblies | Sort-Object) -join "`n")
        documents = @($documentPaths | Sort-Object)
        documentChecksumCount = $documentChecksumCount
        documentChecksumFixture = $documentChecksumFixture
    }
}

function Get-TextSha256([string]$value) {
    return [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($value))).ToLowerInvariant()
}

function Get-SourceUniverse($map) {
    $files = @(
        $map.Values |
            Group-Object { [string]$_['filename'] } |
            Sort-Object Name |
            ForEach-Object {
                $fileLines = @($_.Group)
                $fileBranches = 0
                foreach ($fileLine in $fileLines) {
                    $fileBranches += [int]$fileLine['branchValid']
                }
                [pscustomobject][ordered]@{
                    path = [string]$_.Name
                    linesValid = $fileLines.Count
                    branchesValid = $fileBranches
                }
            }
    )
    $canonical = @($files | ForEach-Object {
        "$($_.path)`n$($_.linesValid)`n$($_.branchesValid)"
    }) -join "`n"
    $digestBytes = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($canonical))
    return [ordered]@{
        fileCount = $files.Count
        linesValid = [int](($files | Measure-Object linesValid -Sum).Sum)
        branchesValid = [int](($files | Measure-Object branchesValid -Sum).Sum)
        fileMetricsSha256 = [Convert]::ToHexString($digestBytes).ToLowerInvariant()
    }
}

function Test-SourceUniverseFingerprint {
    $complete = @{
        'a.cs:1' = [ordered]@{ filename = 'a.cs'; number = 1; hits = 1; branchValid = 0; branchCovered = 0 }
        'b.cs:1' = [ordered]@{ filename = 'b.cs'; number = 1; hits = 0; branchValid = 2; branchCovered = 0 }
    }
    $missingFile = @{
        'a.cs:1' = $complete['a.cs:1']
    }
    $completeUniverse = Get-SourceUniverse $complete
    $missingUniverse = Get-SourceUniverse $missingFile
    if ([int]$completeUniverse.fileCount -ne 2 -or
        [int]$completeUniverse.linesValid -ne 2 -or
        [int]$completeUniverse.branchesValid -ne 2 -or
        [string]$completeUniverse.fileMetricsSha256 -ceq [string]$missingUniverse.fileMetricsSha256) {
        throw 'Coverage source-universe omission fixture did not change the fingerprint.'
    }
}

Test-SourceUniverseFingerprint

$pdbUniverse = Get-PortablePdbUniverse @($index.productionAssemblies)

function Assert-CoverageRunnerBinaryEvidence($assemblyEntries, $reportEntries) {
    $expected = @{}
    foreach ($entry in @($assemblyEntries)) {
        $assembly = [string]$entry.assembly
        if ($expected.ContainsKey($assembly) -or
            [string]$entry.assemblySha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$entry.pdbSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Coverage production binary identity is invalid or duplicated: $assembly"
        }
        $expected[$assembly] = $entry
    }
    foreach ($report in @($reportEntries)) {
        if ($null -eq $report.PSObject.Properties['productionBinaryEvidence']) {
            throw "Coverage report lacks collection-time production binary evidence: $($report.assembly)"
        }
        $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($evidence in @($report.productionBinaryEvidence)) {
            $assembly = [string]$evidence.assembly
            if (-not $seen.Add($assembly) -or -not $expected.ContainsKey($assembly)) {
                throw "Coverage runner binary evidence is unknown or duplicated: runner=$($report.assembly) assembly=$assembly"
            }
            $authoritative = $expected[$assembly]
            if ([string]$evidence.assemblySha256 -cne [string]$authoritative.assemblySha256 -or
                [string]$evidence.pdbSha256 -cne [string]$authoritative.pdbSha256) {
                throw "Coverage runner used stale production assembly/PDB: runner=$($report.assembly) assembly=$assembly"
            }
        }
    }
}

function Assert-CoverageReportIdentitySet($reportEntry, $observedAssemblies) {
    $observed = @($observedAssemblies | Sort-Object -Unique)
    $evidence = @($reportEntry.productionBinaryEvidence | ForEach-Object { [string]$_.assembly } | Sort-Object -Unique)
    if ($observed.Count -ne @($observedAssemblies).Count -or
        $evidence.Count -ne @($reportEntry.productionBinaryEvidence).Count -or
        $observed.Count -ne $evidence.Count -or
        ($observed -join "`n") -cne ($evidence -join "`n")) {
        throw "Cobertura production assembly set does not exactly match collection-time DLL/PDB evidence: runner=$($reportEntry.assembly) observed=$($observed -join ',') evidence=$($evidence -join ',')"
    }
}

$binaryIdentityFixture = @{
    'IIoT.A' = [pscustomobject]@{ assembly = 'IIoT.A'; assemblySha256 = ('a' * 64); pdbSha256 = ('b' * 64) }
}
$staleBinaryFixture = $null
try {
    Assert-CoverageRunnerBinaryEvidence `
        @($binaryIdentityFixture.Values) `
        @([pscustomobject]@{
            assembly = 'FixtureTests'
            productionBinaryEvidence = @([pscustomobject]@{
                assembly = 'IIoT.A'
                assemblySha256 = ('c' * 64)
                pdbSha256 = ('b' * 64)
            })
        })
}
catch {
    $staleBinaryFixture = $_.Exception.Message
}
if ($staleBinaryFixture -notmatch 'used stale production assembly/PDB') {
    throw "Coverage stale-binary fixture did not fail closed: $staleBinaryFixture"
}
Assert-CoverageRunnerBinaryEvidence `
    @($binaryIdentityFixture.Values) `
    @([pscustomobject]@{
        assembly = 'FixtureTests'
        productionBinaryEvidence = @($binaryIdentityFixture.Values)
    })

$missingReportIdentityFixture = $null
try {
    Assert-CoverageReportIdentitySet `
        ([pscustomobject]@{ assembly = 'FixtureTests'; productionBinaryEvidence = @() }) `
        @('IIoT.A')
}
catch {
    $missingReportIdentityFixture = $_.Exception.Message
}
if ($missingReportIdentityFixture -notmatch 'does not exactly match') {
    throw "Coverage report identity-set fixture did not fail closed: $missingReportIdentityFixture"
}
Assert-CoverageReportIdentitySet `
    ([pscustomobject]@{ assembly = 'FixtureTests'; productionBinaryEvidence = @($binaryIdentityFixture.Values) }) `
    @('IIoT.A')
Assert-CoverageRunnerBinaryEvidence @($index.productionAssemblies) @($index.reports)

if ($null -eq $pdbUniverse.documentChecksumFixture -or [int]$pdbUniverse.documentChecksumCount -le 0) {
    throw 'Portable PDB source checksum verification produced no evidence.'
}
$staleSourceFixturePath = Join-Path ([System.IO.Path]::GetTempPath()) "cloud-coverage-stale-source-$([Guid]::NewGuid().ToString('N')).cs"
$staleSourceFixtureFailure = $null
try {
    $fixtureSourceBytes = [System.IO.File]::ReadAllBytes([string]$pdbUniverse.documentChecksumFixture.sourcePath)
    $staleSourceBytes = [byte[]]::new($fixtureSourceBytes.Length + 1)
    [System.Array]::Copy($fixtureSourceBytes, $staleSourceBytes, $fixtureSourceBytes.Length)
    $staleSourceBytes[$staleSourceBytes.Length - 1] = 10
    [System.IO.File]::WriteAllBytes($staleSourceFixturePath, $staleSourceBytes)
    Assert-PortablePdbDocumentChecksum `
        $staleSourceFixturePath `
        ([Guid]$pdbUniverse.documentChecksumFixture.algorithmGuid) `
        ([byte[]]$pdbUniverse.documentChecksumFixture.expectedHash)
}
catch {
    $staleSourceFixtureFailure = $_.Exception.Message
}
finally {
    Remove-Item $staleSourceFixturePath -Force -ErrorAction SilentlyContinue
}
if ($staleSourceFixtureFailure -notmatch 'does not match its built portable PDB document checksum') {
    throw "Coverage stale-source/old-PDB fixture did not fail closed: $staleSourceFixtureFailure"
}

$currentProductionProjects = @(& git -C $repoRoot ls-files --cached --others --exclude-standard -- `
    src/core src/hosts src/infrastructure src/services src/shared |
    Where-Object { $_ -match '^src/(core|hosts|infrastructure|services|shared)/.+\.csproj$' } |
    Where-Object { Test-Path (Join-Path $repoRoot $_) } |
    Sort-Object)
$evaluatedProductionProjects = @($index.productionAssemblies | ForEach-Object { [string]$_.project } | Sort-Object)
Assert-CloudQualityExactSet `
    -Candidate $evaluatedProductionProjects `
    -Base $currentProductionProjects `
    -Label 'coverage production project evaluation'

$currentProductionSources = @(& git -C $repoRoot ls-files --cached --others --exclude-standard -- `
    src/core src/hosts src/infrastructure src/services src/shared |
    Where-Object {
        $_ -match '^src/(core|hosts|infrastructure|services|shared)/.+\.cs$' -and
        $_ -notmatch '(?i)/Migrations/'
    } |
    Where-Object { Test-Path (Join-Path $repoRoot $_) } |
    Sort-Object)
Assert-CloudQualityExactSet `
    -Candidate @($pdbUniverse.documents) `
    -Base $currentProductionSources `
    -Label 'coverage portable-PDB source documents'

$mergedMap = @{}
$runnerMetrics = [System.Collections.Generic.List[object]]::new()
$observedProductionAssemblies = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($reportEntry in @($index.reports | Sort-Object assembly)) {
    $reportPath = Join-Path $resultsRoot (([string]$reportEntry.path) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $reportPath -PathType Leaf)) {
        throw "Coverage report is missing for $($reportEntry.assembly): $reportPath"
    }
    $coverageReport = Get-CoverageReport $reportPath
    $runnerMap = $coverageReport.Map
    Assert-CoverageReportIdentitySet $reportEntry @($coverageReport.ProductionAssemblies)
    foreach ($assembly in @($coverageReport.ProductionAssemblies)) {
        $null = $observedProductionAssemblies.Add([string]$assembly)
    }
    $metrics = Get-Metrics $runnerMap
    $runnerMetrics.Add([ordered]@{ assembly = [string]$reportEntry.assembly; metrics = $metrics })
    foreach ($key in $runnerMap.Keys) {
        $line = $runnerMap[$key]
        if (-not $mergedMap.ContainsKey($key)) {
            $mergedMap[$key] = [ordered]@{
                filename = [string]$line.filename
                number = [int]$line.number
                hits = [int]$line.hits
                branchValid = [int]$line.branchValid
                branchCovered = [int]$line.branchCovered
            }
        } else {
            $existing = $mergedMap[$key]
            $existing.hits = [Math]::Max([int]$existing.hits, [int]$line.hits)
            $existing.branchValid = [Math]::Max([int]$existing.branchValid, [int]$line.branchValid)
            $existing.branchCovered = [Math]::Max([int]$existing.branchCovered, [int]$line.branchCovered)
        }
    }
}
Assert-ProductionAssemblyObservation `
    $evaluatedProductionAssemblies `
    @($observedProductionAssemblies)
# Coverage metrics use the complete evaluated production/PDB universe.  A project
# that no required runner loads remains visible with zero-hit sequence points; it
# cannot disappear merely because Coverlet omitted its assembly from every report.
foreach ($key in $pdbUniverse.map.Keys) {
    if (-not $mergedMap.ContainsKey($key)) {
        $mergedMap[$key] = $pdbUniverse.map[$key]
    }
}
$mergedMetrics = Get-Metrics $mergedMap
$sourceUniverse = Get-SourceUniverse $mergedMap

Write-Host "CLOUD_COVERAGE_OBSERVED lines=$($mergedMetrics.linesCovered)/$($mergedMetrics.linesValid) branches=$($mergedMetrics.branchesCovered)/$($mergedMetrics.branchesValid) lineRate=$($mergedMetrics.lineRate) branchRate=$($mergedMetrics.branchRate) files=$($sourceUniverse.fileCount) sourceDigest=$($sourceUniverse.fileMetricsSha256) productionAssemblies=$($pdbUniverse.assemblyCount) assemblyDigest=$($pdbUniverse.assemblySha256) observedAssemblies=$($observedProductionAssemblies.Count)"

Assert-CloudCoverageObservation -Actual $mergedMetrics -Floor $baseline.merged
if ([int]$sourceUniverse.fileCount -ne [int]$baseline.sourceUniverse.fileCount -or
    [int]$sourceUniverse.linesValid -ne [int]$baseline.sourceUniverse.linesValid -or
    [int]$sourceUniverse.branchesValid -ne [int]$baseline.sourceUniverse.branchesValid -or
    [string]$sourceUniverse.fileMetricsSha256 -cne [string]$baseline.sourceUniverse.fileMetricsSha256) {
    throw "Coverage source universe changed without an explicit baseline update: baseline files=$($baseline.sourceUniverse.fileCount) lines=$($baseline.sourceUniverse.linesValid) branches=$($baseline.sourceUniverse.branchesValid) digest=$($baseline.sourceUniverse.fileMetricsSha256); actual files=$($sourceUniverse.fileCount) lines=$($sourceUniverse.linesValid) branches=$($sourceUniverse.branchesValid) digest=$($sourceUniverse.fileMetricsSha256)."
}
if ([int]$pdbUniverse.assemblyCount -ne [int]$baseline.productionAssemblies.assemblyCount -or
    [string]$pdbUniverse.assemblySha256 -cne [string]$baseline.productionAssemblies.assemblySha256) {
    throw "Evaluated production assembly/PDB universe changed without an explicit baseline update: baseline count=$($baseline.productionAssemblies.assemblyCount) digest=$($baseline.productionAssemblies.assemblySha256); actual count=$($pdbUniverse.assemblyCount) digest=$($pdbUniverse.assemblySha256)."
}

$newCodeMetrics = [ordered]@{
    baseRef = $baseCommit
    comparison = $null
    changedFiles = 0
    filesPresentInCoverage = 0
    allowlistedNonExecutableFiles = @()
    addedLines = 0
    executableLines = 0
    coveredLines = 0
    lineRate = $null
    branchesValid = 0
    branchesCovered = 0
    branchRate = $null
    status = 'BaseRefRequired'
}
if (-not [string]::IsNullOrWhiteSpace($baseCommit)) {
    # A clean CI checkout compares PR base to committed HEAD. A local dirty checkout compares
    # the same base to the complete tracked working tree so uncommitted P0 additions cannot leak.
    $diffOutput = @(& git -C $repoRoot diff --unified=0 --no-color $baseCommit -- `
        src/core src/hosts src/infrastructure src/services src/shared 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to calculate P0 new-code diff from '$baseCommit': $($diffOutput -join [Environment]::NewLine)"
    }
    $addedKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $changedFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $currentFile = $null
    foreach ($line in $diffOutput) {
        $text = [string]$line
        if ($text -eq '+++ /dev/null') {
            $currentFile = $null
            continue
        }
        if ($text -match '^\+\+\+ b/(src/(core|hosts|infrastructure|services|shared)/.+\.cs)$') {
            $currentFile = $Matches[1].Substring(4).Replace('\', '/')
            $null = $changedFiles.Add($Matches[1].Replace('\', '/'))
            continue
        }
        if ($null -ne $currentFile -and $text -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            $start = [int]$Matches[1]
            $count = if ([string]::IsNullOrWhiteSpace($Matches[2])) { 1 } else { [int]$Matches[2] }
            for ($offset = 0; $offset -lt $count; $offset++) {
                $null = $addedKeys.Add("${currentFile}:$($start + $offset)")
            }
        }
    }
    $untrackedFiles = @(& git -C $repoRoot ls-files --others --exclude-standard -- `
        src/core src/hosts src/infrastructure src/services src/shared |
        Where-Object { $_ -match '^src/(core|hosts|infrastructure|services|shared)/.+\.cs$' })
    foreach ($untrackedFile in $untrackedFiles) {
        $relativeFile = ([string]$untrackedFile).Substring(4).Replace('\', '/')
        $null = $changedFiles.Add(([string]$untrackedFile).Replace('\', '/'))
        $sourceLines = @(Get-Content (Join-Path $repoRoot ([string]$untrackedFile)))
        for ($lineNumber = 1; $lineNumber -le $sourceLines.Count; $lineNumber++) {
            $null = $addedKeys.Add("${relativeFile}:$lineNumber")
        }
    }
    $newCodeMetrics.comparison = 'base-ref-to-working-tree (clean CI equals base-ref-to-HEAD)'
    $newCodeMetrics.changedFiles = $changedFiles.Count
    $newCodeMetrics.addedLines = $addedKeys.Count

    $coverageFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($coverageLine in $mergedMap.Values) {
        $null = $coverageFiles.Add("src/$([string]$coverageLine.filename)")
    }
    $nonExecutableAllowlist = @{}
    foreach ($allowlistEntry in @($baseline.newP0Code.nonExecutableFileAllowlist)) {
        $allowlistPath = ([string]$allowlistEntry.path).Replace('\', '/')
        $allowlistClassification = [string]$allowlistEntry.classification
        if ($allowlistPath -notmatch '^src/(core|hosts|infrastructure|services|shared)/.+\.cs$' -or
            $allowlistClassification -notin @('non-executable-source', 'generated-migration-source') -or
            [string]::IsNullOrWhiteSpace([string]$allowlistEntry.reason)) {
            throw "Invalid P0 non-executable source allowlist entry: $allowlistPath"
        }
        if ($allowlistClassification -eq 'generated-migration-source' -and
            $allowlistPath -notmatch '^src/infrastructure/IIoT\.EntityFrameworkCore/Migrations/.+\.cs$') {
            throw "Generated-migration coverage classification is outside the EF migration directory: $allowlistPath"
        }
        $nonExecutableAllowlist[$allowlistPath] = [ordered]@{
            classification = $allowlistClassification
            reason = [string]$allowlistEntry.reason
        }
    }
    $missingCoverageFiles = [System.Collections.Generic.List[string]]::new()
    $allowlistedMissingFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($changedFile in @($changedFiles | Sort-Object)) {
        if ($coverageFiles.Contains($changedFile)) {
            $newCodeMetrics.filesPresentInCoverage++
        } elseif ($nonExecutableAllowlist.ContainsKey($changedFile)) {
            $allowlistedMissingFiles.Add([ordered]@{
                path = $changedFile
                classification = [string]$nonExecutableAllowlist[$changedFile].classification
                reason = [string]$nonExecutableAllowlist[$changedFile].reason
            })
        } else {
            $missingCoverageFiles.Add($changedFile)
        }
    }
    $newCodeMetrics.allowlistedNonExecutableFiles = $allowlistedMissingFiles
    if ($missingCoverageFiles.Count -gt 0) {
        throw "Changed P0 source files are absent from every coverage report and have no precise non-executable allowlist entry: $($missingCoverageFiles -join ', ')"
    }

    $newCodeMetrics.status = if ($addedKeys.Count -eq 0) { 'NoP0SourceAdditions' } else { 'NoExecutableP0Additions' }
    $newLines = @($addedKeys | Where-Object { $mergedMap.ContainsKey($_) } | ForEach-Object { $mergedMap[$_] })
    if ($newLines.Count -gt 0) {
        $newCodeMetrics.executableLines = $newLines.Count
        $newCodeMetrics.coveredLines = @($newLines | Where-Object { [int]$_.hits -gt 0 }).Count
        $newCodeMetrics.lineRate = [Math]::Round($newCodeMetrics.coveredLines / $newCodeMetrics.executableLines, 8)
        foreach ($newLine in $newLines) {
            $newCodeMetrics.branchesValid += [int]$newLine.branchValid
            $newCodeMetrics.branchesCovered += [int]$newLine.branchCovered
        }
        $newCodeMetrics.branchRate = if ($newCodeMetrics.branchesValid -eq 0) { 1.0 } else {
            [Math]::Round($newCodeMetrics.branchesCovered / $newCodeMetrics.branchesValid, 8)
        }
        if ([double]$newCodeMetrics.lineRate -lt [double]$baseline.newP0Code.minimumLineRate -or
            [double]$newCodeMetrics.branchRate -lt [double]$baseline.newP0Code.minimumBranchRate) {
            $uncoveredNewLines = @($newLines |
                Where-Object { [int]$_.hits -le 0 } |
                Sort-Object filename, number |
                ForEach-Object { "$([string]$_.filename):$([int]$_.number)" })
            $uncoveredNewBranches = @($newLines |
                Where-Object { [int]$_.branchCovered -lt [int]$_.branchValid } |
                Sort-Object filename, number |
                ForEach-Object { "$([string]$_.filename):$([int]$_.number)=$([int]$_.branchCovered)/$([int]$_.branchValid)" })
            throw "P0 new-code coverage failed: line=$($newCodeMetrics.lineRate) branch=$($newCodeMetrics.branchRate); uncoveredLines=$($uncoveredNewLines -join ','); uncoveredBranches=$($uncoveredNewBranches -join ',')."
        }
        $newCodeMetrics.status = 'Passed'
    }
}

$unloadedInfrastructureFixture = @{
    'infrastructure/IIoT.Fixture/NewWorker.cs:10' = [ordered]@{
        filename = 'infrastructure/IIoT.Fixture/NewWorker.cs'
        number = 10
        hits = 0
        branchValid = 2
        branchCovered = 0
    }
}
$unloadedInfrastructureMetrics = Get-Metrics $unloadedInfrastructureFixture
if ([double]$unloadedInfrastructureMetrics.lineRate -ge [double]$baseline.newP0Code.minimumLineRate -or
    [double]$unloadedInfrastructureMetrics.branchRate -ge [double]$baseline.newP0Code.minimumBranchRate) {
    throw 'Unloaded infrastructure new-code fixture did not fail the production coverage thresholds.'
}

$summaryPath = Join-Path $resultsRoot 'coverage/cloud-coverage-summary.json'
[ordered]@{
    schemaVersion = 1
    collector = [string]$baseline.collector
    version = [string]$baseline.version
    requiredRunners = $requiredAssemblies.Count
    reports = $reportAssemblies.Count
    merged = $mergedMetrics
    sourceUniverse = $sourceUniverse
    productionAssemblies = [ordered]@{
        assemblyCount = $pdbUniverse.assemblyCount
        assemblySha256 = $pdbUniverse.assemblySha256
        observedAssemblyCount = $observedProductionAssemblies.Count
        observedAssemblies = @($observedProductionAssemblies | Sort-Object)
        evidence = $pdbUniverse.assemblies
    }
    runners = $runnerMetrics
    newP0Code = $newCodeMetrics
} | ConvertTo-Json -Depth 20 | Set-Content $summaryPath -Encoding utf8

Write-Host "CLOUD_COVERAGE_OK runners=$($requiredAssemblies.Count) reports=$($reportAssemblies.Count) lines=$($mergedMetrics.linesCovered)/$($mergedMetrics.linesValid) branches=$($mergedMetrics.branchesCovered)/$($mergedMetrics.branchesValid) lineRate=$($mergedMetrics.lineRate) branchRate=$($mergedMetrics.branchRate) newP0=$($newCodeMetrics.status) output=$summaryPath"
