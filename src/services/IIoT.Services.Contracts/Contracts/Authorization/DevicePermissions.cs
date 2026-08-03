namespace IIoT.Services.Contracts.Authorization;

public static class DevicePermissions
{
    public const string Read = CloudPermissionCatalog.Device.Read;
    public const string Create = CloudPermissionCatalog.Device.Create;
    public const string Update = CloudPermissionCatalog.Device.Update;
    public const string MigrateProcess = CloudPermissionCatalog.Device.MigrateProcess;
    public const string Delete = CloudPermissionCatalog.Device.Delete;
    public const string CascadeDelete = CloudPermissionCatalog.Device.CascadeDelete;
}
