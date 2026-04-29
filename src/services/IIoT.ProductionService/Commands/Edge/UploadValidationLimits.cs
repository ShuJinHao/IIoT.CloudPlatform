namespace IIoT.ProductionService.Commands;

public static class UploadValidationLimits
{
    public const long MaxUploadRequestBodyBytes = 5 * 1024 * 1024;

    public const int MaxDeviceLogItems = 1000;
    public const int MaxInjectionPassItems = 1000;

    public const int MaxRequestIdLength = UploadDeduplicationKeys.MaxRequestIdLength;
    public const int MaxShortCodeLength = 32;
    public const int MaxMediumCodeLength = 128;
    public const int MaxDeviceLogMessageLength = 1024;

    public const int MaxHourlyCapacityCount = 1_000_000;
}
