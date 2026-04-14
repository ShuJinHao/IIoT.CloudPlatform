namespace IIoT.Services.Common.Attributes;

/// <summary>
/// 标记 MediatR Command 需要分布式锁保护。
/// KeyTemplate 中用 {PropertyName} 占位符，Behavior 自动反射插值。
/// </summary>
/// <example>
/// [DistributedLock("iiot:lock:capacity:{DeviceId}:{Date}:{ShiftCode}")]
/// public record UpsertDailyCapacityCommand(...) : IRequest;
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DistributedLockAttribute(string keyTemplate) : Attribute
{
    public string KeyTemplate { get; } = keyTemplate;

    /// <summary>等待获取锁的超时秒数，默认 10 秒</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
