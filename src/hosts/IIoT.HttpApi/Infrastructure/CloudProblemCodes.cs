using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Infrastructure;

public static class CloudProblemCodes
{
    public const string DeviceNotFound = "device_not_found";
    public const string InvalidToken = "invalid_token";
    public const string ForbiddenDeviceScope = "forbidden_device_scope";
    public const string InvalidPayload = "invalid_payload";
    public const string UnknownPassStationType = "unknown_pass_station_type";
    public const string UnsupportedSchemaVersion = "unsupported_schema_version";
    public const string PayloadTooLarge = "payload_too_large";
    public const string ServerError = "server_error";

    public static ProblemDetails AddCode(this ProblemDetails problemDetails, string code)
    {
        problemDetails.Extensions["code"] = code;
        return problemDetails;
    }

    public static string Resolve(int statusCode, PathString path, IReadOnlyCollection<string> errors)
    {
        if (statusCode == StatusCodes.Status401Unauthorized)
            return InvalidToken;

        if (statusCode == StatusCodes.Status403Forbidden)
            return ForbiddenDeviceScope;

        if (statusCode == StatusCodes.Status413PayloadTooLarge)
            return PayloadTooLarge;

        if (statusCode >= StatusCodes.Status500InternalServerError)
            return ServerError;

        var errorText = string.Join('\n', errors);
        if (errorText.Contains("schemaVersion", StringComparison.OrdinalIgnoreCase)
            && errorText.Contains("不受支持", StringComparison.Ordinal))
        {
            return UnsupportedSchemaVersion;
        }

        if (errorText.Contains("过站类型", StringComparison.Ordinal)
            && errorText.Contains("不存在", StringComparison.Ordinal))
        {
            return UnknownPassStationType;
        }

        if (errorText.Contains("设备不存在", StringComparison.Ordinal)
            || errorText.Contains("目标设备不存在", StringComparison.Ordinal))
        {
            return DeviceNotFound;
        }

        if (path.StartsWithSegments("/api/v1/edge", StringComparison.OrdinalIgnoreCase)
            && statusCode == StatusCodes.Status400BadRequest)
        {
            return InvalidPayload;
        }

        return statusCode == StatusCodes.Status404NotFound ? DeviceNotFound : InvalidPayload;
    }
}
