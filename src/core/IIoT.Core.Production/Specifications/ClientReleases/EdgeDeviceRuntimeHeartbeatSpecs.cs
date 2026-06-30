using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class EdgeDeviceRuntimeHeartbeatByIdentitySpec : Specification<EdgeDeviceRuntimeHeartbeat>
{
    public EdgeDeviceRuntimeHeartbeatByIdentitySpec(Guid deviceId, string clientCode)
    {
        var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
        FilterCondition = heartbeat =>
            heartbeat.DeviceId == deviceId
            && heartbeat.ClientCode == normalizedClientCode;
    }
}

public sealed class EdgeDeviceRuntimeHeartbeatsByDevicesSpec : Specification<EdgeDeviceRuntimeHeartbeat>
{
    public EdgeDeviceRuntimeHeartbeatsByDevicesSpec(IReadOnlyCollection<Guid>? deviceIds = null)
    {
        FilterCondition = heartbeat => deviceIds == null || deviceIds.Contains(heartbeat.DeviceId);
        SetOrderBy(heartbeat => heartbeat.ClientCode);
    }
}
