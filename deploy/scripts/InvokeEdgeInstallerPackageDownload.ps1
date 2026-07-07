param(
    [Parameter(Mandatory = $true)]
    [string]$CloudApiBaseUrl,

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

function Normalize-PublicBaseUrl {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw 'BaseUrl is required. It must be the public Gateway origin, for example http://<cloud-host>:81.'
    }

    $uri = [System.Uri]$Value.Trim()
    if (($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') `
        -or -not [string]::IsNullOrEmpty($uri.UserInfo) `
        -or -not [string]::IsNullOrEmpty($uri.Query) `
        -or -not [string]::IsNullOrEmpty($uri.Fragment) `
        -or (-not [string]::IsNullOrEmpty($uri.AbsolutePath) -and $uri.AbsolutePath -ne '/')) {
        throw 'BaseUrl must be the public Gateway origin only. Do not include /api/v1, path, query, or fragment.'
    }

    return $uri.GetLeftPart([System.UriPartial]::Authority).TrimEnd('/')
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
$effectiveBaseUrl = if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $apiUri = [System.Uri]$apiRoot
    Normalize-PublicBaseUrl -Value $apiUri.GetLeftPart([System.UriPartial]::Authority)
} else {
    Normalize-PublicBaseUrl -Value $BaseUrl
}
$uri = "$apiRoot/human/client-releases/installer-package"
$tempFile = Join-Path $outputRoot "IIoT.Edge.Setup.download-$([Guid]::NewGuid().ToString('N')).exe"

$payload = [ordered]@{
    channel = $Channel
    targetRuntime = $TargetRuntime
    hostVersion = if ([string]::IsNullOrWhiteSpace($HostVersion)) { $null } else { $HostVersion }
    baseUrl = $effectiveBaseUrl
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
