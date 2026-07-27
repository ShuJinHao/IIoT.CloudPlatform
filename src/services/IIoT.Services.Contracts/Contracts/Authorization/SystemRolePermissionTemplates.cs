namespace IIoT.Services.Contracts.Authorization;

public static class SystemRolePermissionTemplates
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Templates =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [SystemRoles.DeviceAdmin] =
            [
                DevicePermissions.Read,
                DevicePermissions.Update,
                DeviceClientOverviewPermissions.Read,
                EdgeHostPermissions.Read
            ],
            [SystemRoles.ClientInstallerOperator] =
            [
                DevicePermissions.Read,
                DeviceClientOverviewPermissions.Read,
                EdgeHostPermissions.Read,
                ClientReleasePermissions.Read,
                ClientReleasePermissions.GenerateInstaller
            ],
            [SystemRoles.ClientReleaseManager] =
            [
                DeviceClientOverviewPermissions.Read,
                ClientReleasePermissions.Read,
                ClientReleasePermissions.Publish,
                ClientReleasePermissions.Manage
            ],
            [SystemRoles.ProductionViewer] =
            [
                DevicePermissions.Read,
                DeviceClientOverviewPermissions.Read,
                EdgeHostPermissions.Read,
                ClientReleasePermissions.Read
            ],
            [SystemRoles.RoleAdmin] =
            [
                CloudPermissionCatalog.Role.Read,
                CloudPermissionCatalog.Role.Define,
                CloudPermissionCatalog.Role.Update
            ],
            [SystemRoles.HrAdmin] =
            [
                CloudPermissionCatalog.Role.Read,
                CloudPermissionCatalog.Employee.Read,
                CloudPermissionCatalog.Employee.Onboard,
                CloudPermissionCatalog.Employee.Update,
                CloudPermissionCatalog.Employee.UpdateAccess,
                CloudPermissionCatalog.Employee.Deactivate
            ]
        };

    public static readonly IReadOnlyCollection<string> DeviceAdminRetiredPermissions =
    [
        DevicePermissions.Create,
        DevicePermissions.Delete,
        DevicePermissions.CascadeDelete
    ];
}
