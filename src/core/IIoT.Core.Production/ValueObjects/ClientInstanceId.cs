using System.Diagnostics.CodeAnalysis;

namespace IIoT.Core.Production.ValueObjects;

/// <summary>
/// 上位机实例身份(值对象)
/// 云端唯一性 = MacAddress(物理宿主) + ClientCode(实例编号)
/// 同一台宿主机上可以运行多个上位机实例,因此 MAC 单独不足以唯一标识。
/// </summary>
public readonly record struct ClientInstanceId : IParsable<ClientInstanceId>
{
    public string MacAddress { get; }

    public string ClientCode { get; }

    private ClientInstanceId(string macAddress, string clientCode)
    {
        MacAddress = macAddress;
        ClientCode = clientCode;
    }

    /// <summary>
    /// 默认构造产生的零值实例(MacAddress 与 ClientCode 都为 null)。
    /// 用于在反序列化或未初始化场景下做防御性检查。
    /// </summary>
    public bool IsEmpty => MacAddress is null || ClientCode is null;

    /// <summary>
    /// 工厂方法。强制不变量:
    ///  - MacAddress 非空白,统一转大写并去首尾空格
    ///  - ClientCode 非空白,去首尾空格(保留大小写)
    /// </summary>
    public static ClientInstanceId Create(string macAddress, string clientCode)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            throw new ArgumentException("MacAddress 不能为空。", nameof(macAddress));
        if (string.IsNullOrWhiteSpace(clientCode))
            throw new ArgumentException("ClientCode 不能为空。", nameof(clientCode));

        return new ClientInstanceId(
            macAddress.Trim().ToUpperInvariant(),
            clientCode.Trim());
    }

    /// <summary>
    /// 不抛异常的工厂,反序列化路径使用。
    /// </summary>
    public static bool TryCreate(
        string? macAddress,
        string? clientCode,
        out ClientInstanceId value)
    {
        if (string.IsNullOrWhiteSpace(macAddress) || string.IsNullOrWhiteSpace(clientCode))
        {
            value = default;
            return false;
        }

        value = new ClientInstanceId(
            macAddress.Trim().ToUpperInvariant(),
            clientCode.Trim());
        return true;
    }

    /// <summary>
    /// 序列化格式: "MAC|ClientCode",便于日志、缓存键、消息载荷使用。
    /// </summary>
    public override string ToString() => $"{MacAddress}|{ClientCode}";

    // ── IParsable<ClientInstanceId> ────────────────────────────────────

    public static ClientInstanceId Parse(string s, IFormatProvider? provider = null)
    {
        if (TryParse(s, provider, out var result))
            return result;
        throw new FormatException($"无法解析为 ClientInstanceId: '{s}'。期望格式 'MAC|ClientCode'。");
    }

    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out ClientInstanceId result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var parts = s.Split('|', 2);
        if (parts.Length != 2) return false;

        return TryCreate(parts[0], parts[1], out result);
    }
}