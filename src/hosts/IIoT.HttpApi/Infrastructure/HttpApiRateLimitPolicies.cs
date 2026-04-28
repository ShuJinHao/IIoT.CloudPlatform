namespace IIoT.HttpApi.Infrastructure;

public static class HttpApiRateLimitPolicies
{
    public const string GeneralApi = "general-api";
    public const string PasswordLogin = "password-login";
    public const string Refresh = "refresh";
    public const string EdgeOperatorLogin = "edge-operator-login";
    public const string Bootstrap = "bootstrap";
    public const string CapacityUpload = "capacity-upload";
    public const string DeviceLogUpload = "device-log-upload";
    public const string PassStationUpload = "pass-station-upload";
}
