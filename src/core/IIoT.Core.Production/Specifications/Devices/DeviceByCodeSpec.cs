using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 按云端下发的设备 Code 查询单个设备。
/// </summary>
public sealed class DeviceByCodeSpec : Specification<Device>
{
    public DeviceByCodeSpec(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        FilterCondition = device => device.Code == normalizedCode;
    }
}
