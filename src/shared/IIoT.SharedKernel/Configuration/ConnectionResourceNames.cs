namespace IIoT.SharedKernel.Configuration;

/// <summary>
/// 集中维护基础设施连接资源名，避免在多个模块散落裸字符串。
/// 这些名字是代码级资源键，不是运行时秘密。
/// </summary>
public static class ConnectionResourceNames
{
    public const string IiotDatabase = "iiot-db";
    public const string EventBus = "eventbus";
}
