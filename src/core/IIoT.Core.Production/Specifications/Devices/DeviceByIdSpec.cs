using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 按 Id 精确获取单个 Device 聚合根。
/// 命令端用例从 Repository 取出聚合根做修改时使用。
/// </summary>
public sealed class DeviceByIdSpec : Specification<Device>
{
    public DeviceByIdSpec(Guid deviceId)
    {
        FilterCondition = d => d.Id == deviceId;
    }
}