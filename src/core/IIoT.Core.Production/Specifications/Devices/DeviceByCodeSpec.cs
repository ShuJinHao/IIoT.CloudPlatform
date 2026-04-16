using IIoT.Core.Production.Aggregates.Devices;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Devices;

/// <summary>
/// Finds a device by its cloud-issued code.
/// </summary>
public sealed class DeviceByCodeSpec : Specification<Device>
{
    public DeviceByCodeSpec(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        FilterCondition = device => device.Code == normalizedCode;
    }
}
