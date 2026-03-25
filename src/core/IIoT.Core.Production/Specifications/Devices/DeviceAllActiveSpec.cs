using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 专用查询规约：获取全量活跃设备 (供管辖权分配等下拉选择器使用)
/// </summary>
public class DeviceAllActiveSpec : Specification<Device>
{
    public DeviceAllActiveSpec()
    {
        // 只查启用状态的设备
        FilterCondition = d => d.IsActive;

        // 按设备编号排序，保证前端下拉列表稳定有序
        SetOrderBy(d => d.DeviceCode);
    }
}
