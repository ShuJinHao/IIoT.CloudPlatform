Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Require-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -notmatch $Pattern) { throw $Message }
}
function Forbid-Text([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $Path)
    if ($text -match $Pattern) { throw $Message }
}

Forbid-Text 'Directory.Build.targets' 'ValidateCloudDeploymentPolicy|deploy/tests/TestDeploymentPolicy\.ps1' 'Ordinary Cloud builds must not run DeploymentContract tests through an MSBuild hook.'
Require-Text 'deploy/scripts/build-and-push.sh' 'local services_csv=' 'Cloud image builder must preserve an explicit service set.'
Require-Text 'deploy/scripts/build-and-push.sh' 'httpapi\|iiot-httpapi' 'Cloud image builder lost its service allowlist.'
Require-Text '.github/workflows/cloud-routine-request.yml' 'name:\s*cloud-production-state-inspect' 'Cloud routine workflow must be physically limited to read-only production-state inspection.'
Require-Text '.github/workflows/cloud-routine-request.yml' 'INSPECT_CLOUD_STATE' 'Cloud production-state inspection must require its explicit read-only confirmation.'
Require-Text '.github/workflows/cloud-routine-request.yml' 'Upload current production state' 'Cloud production-state inspection must export an allowlisted receipt.'
Forbid-Text '.github/workflows/cloud-routine-request.yml' 'request_base64|request_sha256|operation:\s*|Deliver immutable request|--request-stdin|inputs\.operation' 'Cloud GitHub Actions transport must not retain a manually dispatchable deployment operation.'
Require-Text 'docs/云端规则.md' '只构建.*受影响|受影响.*镜像' 'Cloud incremental image deployment red line is missing.'
Write-Host 'Cloud deployment policy architecture test passed.'
