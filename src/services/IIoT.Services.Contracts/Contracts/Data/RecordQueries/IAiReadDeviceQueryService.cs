namespace IIoT.Services.Contracts.RecordQueries;

public sealed record AiReadDeviceQueryItem(
    Guid Id,
    string DeviceCode,
    string DeviceName,
    Guid ProcessId);

public sealed record AiReadDeviceQueryRequest(
    Guid? DeviceId,
    string? DeviceCode,
    Guid? ProcessId,
    string? Keyword,
    IReadOnlyCollection<Guid>? AllowedDeviceIds,
    int Skip,
    int Take);

/// <summary>
/// AiRead 设备主数据专用读服务。
/// 授权范围、精确条件、模糊条件、计数和分页必须在同一数据库查询中完成。
/// </summary>
public interface IAiReadDeviceQueryService
{
    Task<(IReadOnlyList<AiReadDeviceQueryItem> Items, int TotalCount)> GetPagedAsync(
        AiReadDeviceQueryRequest request,
        CancellationToken cancellationToken = default);
}
