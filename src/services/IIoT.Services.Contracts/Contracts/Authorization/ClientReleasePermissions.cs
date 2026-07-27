namespace IIoT.Services.Contracts.Authorization;

public static class ClientReleasePermissions
{
    public const string Read = CloudPermissionCatalog.ClientRelease.Read;
    public const string GenerateInstaller = CloudPermissionCatalog.ClientRelease.GenerateInstaller;
    public const string Publish = CloudPermissionCatalog.ClientRelease.Publish;
    public const string Manage = CloudPermissionCatalog.ClientRelease.Manage;
    public const string HardDelete = CloudPermissionCatalog.ClientRelease.HardDelete;
}
