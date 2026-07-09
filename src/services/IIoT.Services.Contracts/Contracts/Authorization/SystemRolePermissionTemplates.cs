namespace IIoT.Services.Contracts.Authorization;

public static class SystemRolePermissionTemplates
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Templates =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [SystemRoles.DeviceAdmin] =
            [
                DevicePermissions.Read,
                DevicePermissions.Create,
                DevicePermissions.Update,
                DevicePermissions.Delete,
                DevicePermissions.CascadeDelete,
                EdgeHostPermissions.Read
            ],
            [SystemRoles.ClientInstallerOperator] =
            [
                DevicePermissions.Read,
                EdgeHostPermissions.Read,
                ClientReleasePermissions.Read,
                ClientReleasePermissions.GenerateInstaller
            ],
            [SystemRoles.ClientReleaseManager] =
            [
                ClientReleasePermissions.Read,
                ClientReleasePermissions.Publish,
                ClientReleasePermissions.Manage
            ],
            [SystemRoles.ProductionViewer] =
            [
                DevicePermissions.Read,
                EdgeHostPermissions.Read,
                ClientReleasePermissions.Read
            ],
            [SystemRoles.RoleAdmin] =
            [
                "Role.Read",
                "Role.Define",
                "Role.Update"
            ],
            [SystemRoles.HrAdmin] =
            [
                "Role.Read",
                "Employee.Read",
                "Employee.Onboard",
                "Employee.Update",
                "Employee.UpdateAccess"
            ]
        };
}
