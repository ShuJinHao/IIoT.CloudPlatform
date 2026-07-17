[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$validator = Join-Path $PSScriptRoot 'Test-ThreeRepositoryClosurePredicates.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("three-repository-closure-fixtures-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    $evidencePath = Join-Path $tempRoot 'evidence.json'
    $rulePath = Join-Path $tempRoot 'formal-rule.md'
    Set-Content $evidencePath '{"status":"PASS"}' -Encoding utf8
    Set-Content $rulePath '# Formal rule' -Encoding utf8
    $evidenceSha = (Get-FileHash $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()

    function New-Predicate([int]$number) {
        return [ordered]@{
            id = "CLOSURE-{0:d2}" -f $number
            blocking = $true
            description = "Closed predicate $number"
            inputUniverse = [ordered]@{
                closed = $true
                roots = @('src')
                globs = @('**/*')
                allowlist = @([ordered]@{ value = 'none'; reason = 'Fixture has no exclusions.' })
                denylist = @('forbidden-fixture')
            }
            commands = @('fixture-command')
            expectation = [ordered]@{ exitCode = 0; threshold = 'exact' }
            actual = [ordered]@{ status = 'PASS'; exitCode = 0; reasonCode = 'FIXTURE_PASS' }
            bindings = [ordered]@{
                repositories = @([ordered]@{
                    repository = 'Edge'; head = ('a' * 40); tree = ('b' * 40); clean = $true
                })
                github = @([ordered]@{ repository = 'fixture'; pr = 1; run = 1; job = 'required'; artifact = 1 })
            }
            evidence = @([ordered]@{ path = $evidencePath; sha256 = $evidenceSha })
            rules = @([ordered]@{ formalLocation = $rulePath; gate = 'fixture-command' })
        }
    }

    function New-Catalog {
        return [ordered]@{
            schemaVersion = 1
            predicates = @(1..14 | ForEach-Object { New-Predicate $_ })
        }
    }

    function Write-Catalog($catalog, [string]$name) {
        $path = Join-Path $tempRoot "$name.json"
        $catalog | ConvertTo-Json -Depth 100 | Set-Content $path -Encoding utf8
        return $path
    }

    function Invoke-Validator([string]$catalogPath, [bool]$skipBinding) {
        $output = Join-Path $tempRoot ([IO.Path]::GetFileNameWithoutExtension($catalogPath) + '-result.json')
        $arguments = @(
            '-NoProfile', '-File', $validator,
            '-CatalogPath', $catalogPath,
            '-OutputPath', $output,
            '-EdgeRepositoryRoot', (Join-Path $workspaceRoot '.codex-worktrees/edge-startup-exception-retirement-closure'),
            '-CloudRepositoryRoot', (Join-Path $workspaceRoot '.codex-worktrees/cloud-cache-001'),
            '-AiRepositoryRoot', (Join-Path $workspaceRoot '.codex-worktrees/ai-phase0-closeout')
        )
        if ($skipBinding) { $arguments += '-SkipRepositoryBinding' }
        $validationOutput = @(& pwsh @arguments 2>&1)
        if ($LASTEXITCODE -ne 0 -and [IO.Path]::GetFileNameWithoutExtension($catalogPath) -eq 'positive') {
            $validationOutput | ForEach-Object { Write-Host $_ }
        }
        return $LASTEXITCODE
    }

    $positivePath = Write-Catalog (New-Catalog) 'positive'
    if ((Invoke-Validator $positivePath $true) -ne 0) { throw 'Positive closure catalog fixture failed.' }

    $scenarios = @(
        [ordered]@{ name = 'missing-id'; mutate = { param($c) $c.predicates = @($c.predicates | Select-Object -Skip 1) } },
        [ordered]@{ name = 'duplicate-id'; mutate = { param($c) $c.predicates[1].id = $c.predicates[0].id } },
        [ordered]@{ name = 'empty-universe'; mutate = { param($c) $c.predicates[0].inputUniverse.roots = @() } },
        [ordered]@{ name = 'unexplained-allowlist'; mutate = { param($c) $c.predicates[0].inputUniverse.allowlist[0].reason = '' } },
        [ordered]@{ name = 'evidence-hash'; mutate = { param($c) $c.predicates[0].evidence[0].sha256 = ('f' * 64) } },
        [ordered]@{ name = 'blocking-not-run'; mutate = { param($c) $c.predicates[0].actual.status = 'NOT-RUN' } },
        [ordered]@{ name = 'blocking-fail'; mutate = { param($c) $c.predicates[0].actual.status = 'FAIL' } },
        [ordered]@{ name = 'retrospective-only-rule'; mutate = { param($c) $c.predicates[0].rules[0].formalLocation = 'docs/改动复盘与规则沉淀.md' } },
        [ordered]@{ name = 'unclosed-universal'; mutate = { param($c) $c.predicates[0].description = '所有输入稳定'; $c.predicates[0].inputUniverse.closed = $false } }
    )
    foreach ($scenario in $scenarios) {
        $catalog = New-Catalog
        & $scenario.mutate $catalog
        $path = Write-Catalog $catalog ([string]$scenario.name)
        if ((Invoke-Validator $path $true) -eq 0) { throw "$($scenario.name) fixture did not fail closed." }
    }

    $headMismatch = New-Catalog
    foreach ($predicate in @($headMismatch.predicates)) {
        $predicate.bindings.repositories = @([ordered]@{
            repository = 'Edge'; head = ('0' * 40); tree = ('0' * 40); clean = $true
        })
    }
    $headMismatchPath = Write-Catalog $headMismatch 'candidate-head-mismatch'
    if ((Invoke-Validator $headMismatchPath $false) -eq 0) {
        throw 'Candidate HEAD mismatch fixture did not fail closed.'
    }

    Write-Host 'THREE_REPOSITORY_CLOSURE_BEHAVIOR_OK positive=1 negative=10'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
