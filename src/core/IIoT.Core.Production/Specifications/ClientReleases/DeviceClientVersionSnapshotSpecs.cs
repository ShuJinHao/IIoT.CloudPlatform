using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class DeviceClientVersionSnapshotByDeviceSpec : Specification<DeviceClientVersionSnapshot>
{
    public DeviceClientVersionSnapshotByDeviceSpec(Guid deviceId)
    {
        FilterCondition = snapshot => snapshot.DeviceId == deviceId;
        AddInclude(snapshot => snapshot.InstalledPlugins);
    }
}

public sealed class DeviceClientVersionSnapshotsByDevicesSpec : Specification<DeviceClientVersionSnapshot>
{
    public DeviceClientVersionSnapshotsByDevicesSpec(IReadOnlyCollection<Guid>? deviceIds = null)
    {
        FilterCondition = snapshot => deviceIds == null || deviceIds.Contains(snapshot.DeviceId);
        AddInclude(snapshot => snapshot.InstalledPlugins);
        SetOrderBy(snapshot => snapshot.ClientCode);
    }
}
