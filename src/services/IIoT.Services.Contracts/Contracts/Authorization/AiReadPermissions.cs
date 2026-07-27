namespace IIoT.Services.Contracts.Authorization;

public static class AiReadPermissions
{
    public const string Device = CloudPermissionCatalog.AiRead.Device;
    public const string Process = CloudPermissionCatalog.AiRead.Process;
    public const string ClientRelease = CloudPermissionCatalog.AiRead.ClientRelease;
    public const string DeviceClientState = CloudPermissionCatalog.AiRead.DeviceClientState;
    public const string Capacity = CloudPermissionCatalog.AiRead.Capacity;
    public const string DeviceLog = CloudPermissionCatalog.AiRead.DeviceLog;
    public const string ProductionRecord = CloudPermissionCatalog.AiRead.ProductionRecord;
    public const string IdentityStatus = CloudPermissionCatalog.AiRead.IdentityStatus;
}
