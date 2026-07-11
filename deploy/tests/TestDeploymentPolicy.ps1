Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}

Require-Text 'deploy/scripts/build-and-push.sh' 'local services_csv=' 'Cloud image builder must preserve an explicit service set.'
Require-Text 'deploy/scripts/build-and-push.sh' 'httpapi\|iiot-httpapi' 'Cloud image builder lost its service allowlist.'
Require-Text '.github/workflows/cloud-routine-request.yml' 'operation:\s*[\s\S]*?- deploy\s*[\s\S]*?- inspect' 'Cloud routine workflow must expose read-only production-state inspection.'
Require-Text '.github/workflows/cloud-routine-request.yml' "if: inputs\.operation == 'deploy'" 'Cloud deployment request must be gated by operation=deploy.'
Require-Text 'docs/云端规则.md' '只构建.*受影响|受影响.*镜像' 'Cloud incremental image deployment red line is missing.'
Write-Host 'Cloud deployment policy architecture test passed.'
