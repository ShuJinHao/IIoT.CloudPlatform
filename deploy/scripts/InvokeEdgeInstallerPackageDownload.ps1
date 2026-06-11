param(
    [string]$CloudApiBaseUrl = 'http://10.98.90.154:81/api/v1',

    [Parameter(Mandatory = $true)]
    [string]$CloudToken,

    [Parameter(Mandatory = $true)]
    [Guid]$DeviceId,

    [string]$ModuleId = 'Homogenization',

    [string]$Channel = 'stable',

    [string]$TargetRuntime = 'win-x64',

    [string]$HostVersion,

    [string]$BaseUrl,

    [string]$OutputDirectory = '.',

    [switch]$ConfirmSecretRotation
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmSecretRotation) {
    throw 'This command rotates the selected device bootstrap secret. Re-run with -ConfirmSecretRotation after confirming this is the intended test/deployment device.'
}

function Resolve-DownloadPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return [System.IO.Path]::GetFullPath($PathValue)
}

function Get-ContentDispositionFileName {
    param([string]$ContentDisposition)

    if ([string]::IsNullOrWhiteSpace($ContentDisposition)) {
        return $null
    }

    $encoded = [regex]::Match($ContentDisposition, "filename\*=UTF-8''([^;]+)", 'IgnoreCase')
    if ($encoded.Success) {
        return [System.Uri]::UnescapeDataString($encoded.Groups[1].Value.Trim('"'))
    }

    $plain = [regex]::Match($ContentDisposition, 'filename="?([^";]+)"?', 'IgnoreCase')
    if ($plain.Success) {
        return $plain.Groups[1].Value.Trim('"')
    }

    return $null
}

function Assert-InstallerPayloadMarker {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $magic = [System.Text.Encoding]::ASCII.GetBytes('IIOTEDG1')
    $stream = [System.IO.File]::OpenRead($PathValue)
    try {
        if ($stream.Length -lt 16) {
            throw 'Downloaded file is too small to be an Edge installer package.'
        }

        $trailer = [byte[]]::new(16)
        [void]$stream.Seek(-16, [System.IO.SeekOrigin]::End)
        $offset = 0
        while ($offset -lt 16) {
            $read = $stream.Read($trailer, $offset, 16 - $offset)
            if ($read -le 0) {
                throw 'Failed to read installer trailer.'
            }
            $offset += $read
        }

        for ($i = 0; $i -lt $magic.Length; $i++) {
            if ($trailer[8 + $i] -ne $magic[$i]) {
                throw 'Downloaded file does not contain the IIoT installer payload marker. It is not the configured Cloud-generated .exe package.'
            }
        }
    }
    finally {
        $stream.Dispose()
    }
}

$outputRoot = Resolve-DownloadPath -PathValue $OutputDirectory
New-Item -Path $outputRoot -ItemType Directory -Force | Out-Null

$apiRoot = $CloudApiBaseUrl.TrimEnd('/')
$uri = "$apiRoot/human/client-releases/installer-package"
$tempFile = Join-Path $outputRoot "IIoT.Edge.Setup.download-$([Guid]::NewGuid().ToString('N')).exe"

$payload = [ordered]@{
    channel = $Channel
    targetRuntime = $TargetRuntime
    hostVersion = if ([string]::IsNullOrWhiteSpace($HostVersion)) { $null } else { $HostVersion }
    baseUrl = if ([string]::IsNullOrWhiteSpace($BaseUrl)) { $null } else { $BaseUrl }
    selections = @(
        [ordered]@{
            moduleId = $ModuleId
            deviceId = $DeviceId
        }
    )
}

$headers = @{
    Authorization = "Bearer $CloudToken"
}

$response = Invoke-WebRequest `
    -Method Post `
    -Uri $uri `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body ($payload | ConvertTo-Json -Depth 20) `
    -OutFile $tempFile `
    -PassThru

$fileName = Get-ContentDispositionFileName -ContentDisposition ([string]$response.Headers['Content-Disposition'])
if ([string]::IsNullOrWhiteSpace($fileName)) {
    $fileName = "IIoT.Edge.Setup.$Channel.$($DeviceId.ToString('N')).exe"
}

if (-not $fileName.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Downloaded package file name is not .exe: $fileName"
}

$targetFile = Join-Path $outputRoot ([System.IO.Path]::GetFileName($fileName))
if (Test-Path $targetFile) {
    Remove-Item -Path $targetFile -Force
}

Move-Item -Path $tempFile -Destination $targetFile
Assert-InstallerPayloadMarker -PathValue $targetFile

$hash = (Get-FileHash -Algorithm SHA256 -Path $targetFile).Hash.ToLowerInvariant()
Write-Host "Downloaded Edge installer package: $targetFile"
Write-Host "sha256=$hash"
