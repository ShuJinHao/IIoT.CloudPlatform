using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 专用查询规约：根据物理 MAC 地址精确查询设备
/// </summary>
public class DeviceByMacSpec : Specification<Device>
{
    public DeviceByMacSpec(string macAddress)
    {
        // 1. 设置过滤条件
        FilterCondition = d => d.MacAddress == macAddress;

        // 💡 如果后续设备聚合根里加了导航属性需要 Include，
        // 直接在这里 AddInclude 即可，Handler 层完全不用改代码！
    }
}