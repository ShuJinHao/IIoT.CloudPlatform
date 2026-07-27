using System.Security.Claims;
using FluentAssertions;
using IIoT.MigrationWorkApp.SeedData;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class SystemRolePermissionSeedTests
{
    [Fact]
    public void DeviceAdminCleanup_ShouldSelectOnlyThreeRetiredPermissionClaims()
    {
        Claim[] claims =
        [
            new(IIoTClaimTypes.Permission, " Device.Create "),
            new(IIoTClaimTypes.Permission, "device.delete"),
            new(IIoTClaimTypes.Permission, "DEVICE.CASCADEDELETE"),
            new(IIoTClaimTypes.Permission, DevicePermissions.Update),
            new(IIoTClaimTypes.Permission, "Custom.Device.Permission"),
            new("OtherClaim", DevicePermissions.Delete)
        ];

        var selected = SystemInitData.SelectRetiredDeviceAdminPermissionClaims(
            " deviceadmin ",
            claims);

        selected.Should().HaveCount(3);
        selected.Select(claim => claim.Value.Trim()).Should().BeEquivalentTo(
            DevicePermissions.Create,
            "device.delete",
            "DEVICE.CASCADEDELETE");
        selected.Should().NotContain(claim => claim.Value == DevicePermissions.Update);
        selected.Should().NotContain(claim => claim.Value == "Custom.Device.Permission");
        selected.Should().NotContain(claim => claim.Type == "OtherClaim");
    }

    [Fact]
    public void DeviceAdminCleanup_ShouldNeverTouchCustomRoles()
    {
        Claim[] claims =
        [
            new(IIoTClaimTypes.Permission, DevicePermissions.Create),
            new(IIoTClaimTypes.Permission, DevicePermissions.Delete),
            new(IIoTClaimTypes.Permission, DevicePermissions.CascadeDelete)
        ];

        var selected = SystemInitData.SelectRetiredDeviceAdminPermissionClaims(
            "LineDeviceSupervisor",
            claims);

        selected.Should().BeEmpty();
    }
}
