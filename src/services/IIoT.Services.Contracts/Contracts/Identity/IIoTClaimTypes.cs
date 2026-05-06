namespace IIoT.Services.Contracts.Identity;

/// <summary>
/// IIoT 云端统一使用的 JWT Claim 类型定义。
/// </summary>
public static class IIoTClaimTypes
{
    public const string ActorType = "actor_type";
    public const string DeviceId = "device_id";
    public const string ClientCode = "client_code";
    public const string ProcessId = "process_id";
    public const string DelegatedUserId = "delegated_user_id";
    public const string DelegatedDeviceId = "delegated_device_id";
    public const string Permission = "Permission";
    public const string EdgeDeviceActor = "edge-device";
    public const string HumanActor = "human-user";
    public const string AiServiceActor = "ai-service-account";
}
