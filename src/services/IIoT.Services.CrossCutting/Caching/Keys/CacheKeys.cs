namespace IIoT.Services.CrossCutting.Caching;

public static class CacheKeys
{
    public static string PermissionByUser(Guid userId) => $"iiot:permissions:v1:user:{userId}";

    public static string AllDefinedPermissions() => "iiot:permissions:v1:all-defined";

    public static string PermissionByUserPattern() => "iiot:permissions:v1:user:*";

    public static string ProcessesAll() => "iiot:master-data:processes:v1:all";

    public static string DeviceCode(string code) => $"iiot:device:code:v1:{code.Trim().ToUpperInvariant()}";

    public static string DeviceIdentity(Guid deviceId) => $"iiot:device:identity:v1:{deviceId}";

    public static string AllDevices() => "iiot:devices:v1:all-active";

    public static string DevicesByProcess(Guid processId) => $"iiot:devices:process:v1:{processId}";

    public static string Recipe(Guid recipeId) => $"iiot:recipe:v1:{recipeId}";

    public static string RecipesByProcess(Guid processId) => $"iiot:recipes:process:v1:{processId}";

    public static string RecipesByDevice(Guid deviceId) => $"iiot:recipes:device:v1:{deviceId}";

    public static string CapacityHourly(Guid deviceId, DateOnly date, string? plcName) =>
        $"iiot:capacity:hourly:v1:{deviceId}:{date:yyyyMMdd}:{plcName ?? "all"}";

    public static string CapacityHourlyPattern(Guid deviceId) => $"iiot:capacity:hourly:v1:{deviceId}:*";

    public static string CapacitySummary(Guid deviceId, DateOnly date, string? plcName) =>
        $"iiot:capacity:summary:v1:{deviceId}:{date:yyyyMMdd}:{plcName ?? "all"}";

    public static string CapacitySummaryPattern(Guid deviceId) => $"iiot:capacity:summary:v1:{deviceId}:*";

    public static string CapacityRange(Guid deviceId, DateOnly startDate, DateOnly endDate, string? plcName) =>
        $"iiot:capacity:range:v1:{deviceId}:{startDate:yyyyMMdd}:{endDate:yyyyMMdd}:{plcName ?? "all"}";

    public static string CapacityRangePattern(Guid deviceId) => $"iiot:capacity:range:v1:{deviceId}:*";

    public static string CapacityPaged(DateOnly? date, Guid? deviceId, int pageNumber, int pageSize) =>
        $"iiot:capacity:paged:v1:{date:yyyyMMdd}:{deviceId}:{pageNumber}:{pageSize}";

    public static string CapacityPagedByDevicePattern(Guid deviceId) => $"iiot:capacity:paged:v1:*:{deviceId}:*:*";

    public static string DeviceAccessesByUser(Guid userId) => $"iiot:device-access:user:{userId}";
}
