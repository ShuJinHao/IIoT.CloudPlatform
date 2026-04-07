using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

public class DeviceByMacAndClientCodeSpec : Specification<Device>
{
    public DeviceByMacAndClientCodeSpec(string macAddress, string clientCode)
    {
        FilterCondition = d => d.MacAddress == macAddress && d.ClientCode == clientCode;
    }
}