using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class DeviceClientStateByIdentitySpec : Specification<DeviceClientState>
{
    public DeviceClientStateByIdentitySpec(Guid deviceId, string clientCode)
    {
        var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
        FilterCondition = state =>
            state.DeviceId == deviceId
            && state.ClientCode == normalizedClientCode;
    }
}

public sealed class DeviceClientStatesByDevicesSpec : Specification<DeviceClientState>
{
    public DeviceClientStatesByDevicesSpec(IReadOnlyCollection<Guid>? deviceIds = null)
    {
        FilterCondition = state => deviceIds == null || deviceIds.Contains(state.DeviceId);
        SetOrderBy(state => state.ClientCode);
    }
}
