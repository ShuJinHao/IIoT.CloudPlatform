using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.ValueObjects;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// 按上位机实例身份(MacAddress + ClientCode)精确查询单台设备。
/// </summary>
public class DeviceByClientInstanceIdSpec : Specification<Device>
{
    public DeviceByClientInstanceIdSpec(ClientInstanceId instance)
    {
        var mac = instance.MacAddress;
        var code = instance.ClientCode;

        FilterCondition = d =>
            d.Instance.MacAddress == mac &&
            d.Instance.ClientCode == code;
    }
}