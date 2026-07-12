Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CloudTestTypeIdentity {
    param([Parameter(Mandatory)][object]$Type)

    $assemblyName = [string]$Type.Assembly.GetName().Name
    if ($assemblyName -in @('System.Private.CoreLib', 'System.Runtime', 'mscorlib', 'netstandard')) {
        return "framework::$([string]$Type.FullName)"
    }
    return "$assemblyName::$([string]$Type.FullName)"
}

function ConvertTo-CloudTestInvariantText {
    param([Parameter(Mandatory)][object]$Value)

    if ($Value -is [System.IFormattable]) {
        return [string]$Value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
    }
    return [string]$Value
}

function ConvertTo-CloudTestAttributeValueNode {
    param([Parameter(Mandatory)][object]$Argument)

    $argumentType = $Argument.ArgumentType
    $typeIdentity = Get-CloudTestTypeIdentity -Type $argumentType
    $value = $Argument.Value

    if ($null -eq $value) {
        return [ordered]@{ kind = 'null'; type = $typeIdentity }
    }

    if ($argumentType.IsArray) {
        return [ordered]@{
            kind = 'array'
            type = $typeIdentity
            values = [object[]]@($value | ForEach-Object { ConvertTo-CloudTestAttributeValueNode -Argument $_ })
        }
    }

    if ($argumentType.IsEnum) {
        return [ordered]@{
            kind = 'enum'
            type = $typeIdentity
            value = ConvertTo-CloudTestInvariantText -Value $value
        }
    }

    if ([string]$argumentType.FullName -eq 'System.Type') {
        return [ordered]@{
            kind = 'type'
            type = $typeIdentity
            value = Get-CloudTestTypeIdentity -Type $value
        }
    }

    $kind = switch ([string]$argumentType.FullName) {
        'System.String' { 'string' }
        'System.Char' { 'char' }
        'System.Boolean' { 'boolean' }
        'System.Single' { 'single' }
        'System.Double' { 'double' }
        default { 'scalar' }
    }
    $canonicalValue = switch ($kind) {
        # Attribute strings are runtime data, not repository paths. Preserve the
        # exact UTF-16 code units so canonically equivalent but ordinally distinct
        # values cannot collapse to the same governance signature.
        'string' { [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes([string]$value)) }
        'char' { [int][char]$value }
        'boolean' { if ([bool]$value) { 'true' } else { 'false' } }
        'single' { ([BitConverter]::SingleToInt32Bits([single]$value)).ToString('x8', [Globalization.CultureInfo]::InvariantCulture) }
        'double' { ([BitConverter]::DoubleToInt64Bits([double]$value)).ToString('x16', [Globalization.CultureInfo]::InvariantCulture) }
        default { ConvertTo-CloudTestInvariantText -Value $value }
    }
    $node = [ordered]@{ kind = $kind; type = $typeIdentity; value = $canonicalValue }
    if ($kind -eq 'string') {
        $node.encoding = 'utf16le-base64'
    }
    return $node
}

function Get-CloudTestCustomAttributeSignature {
    param([Parameter(Mandatory)][object]$Attribute)

    $namedArguments = @($Attribute.NamedArguments | ForEach-Object {
        [ordered]@{
            memberKind = if ($_.IsField) { 'field' } else { 'property' }
            memberName = [string]$_.MemberName
            value = ConvertTo-CloudTestAttributeValueNode -Argument $_.TypedValue
        }
    } | Sort-Object memberName, memberKind)
    $payload = [ordered]@{
        schema = 'cloud-cad-v1'
        attributeType = Get-CloudTestTypeIdentity -Type $Attribute.AttributeType
        constructorArguments = [object[]]@($Attribute.ConstructorArguments | ForEach-Object {
            ConvertTo-CloudTestAttributeValueNode -Argument $_
        })
        namedArguments = [object[]]$namedArguments
    }
    $canonicalJson = $payload | ConvertTo-Json -Depth 40 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonicalJson)
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    return "cloud-cad-v1:$digest"
}

Export-ModuleMember -Function Get-CloudTestCustomAttributeSignature
