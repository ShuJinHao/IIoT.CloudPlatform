using System.Text.Json;
using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 成功生成的客户端首装包不可变证据。记录只保存公开身份与哈希事实，
/// 不保存 bootstrap secret、secret hash 或包内容。
/// </summary>
public sealed class EdgeInstallerGenerationRecord : BaseEntity<Guid>
{
    private EdgeInstallerGenerationRecord()
    {
    }

    public EdgeInstallerGenerationRecord(
        Guid generationId,
        Guid? operatorUserId,
        string? operatorName,
        DateTime generatedAtUtc,
        string channel,
        string targetRuntime,
        string hostVersion,
        string hostSha256,
        string fileName,
        string packageSha256,
        long packageSize,
        IEnumerable<EdgeInstallerGenerationBindingFact> bindings,
        IEnumerable<EdgeInstallerGenerationPluginFact> plugins)
    {
        if (generationId == Guid.Empty)
        {
            throw new ArgumentException("Generation id cannot be empty.", nameof(generationId));
        }

        if (packageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packageSize));
        }

        Id = generationId;
        OperatorUserId = operatorUserId;
        OperatorName = NormalizeOptional(operatorName);
        GeneratedAtUtc = NormalizeUtc(generatedAtUtc);
        Channel = NormalizeRequired(channel, nameof(channel));
        TargetRuntime = NormalizeRequired(targetRuntime, nameof(targetRuntime));
        HostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        HostSha256 = NormalizeSha256(hostSha256, nameof(hostSha256));
        FileName = NormalizeRequired(fileName, nameof(fileName));
        PackageSha256 = NormalizeSha256(packageSha256, nameof(packageSha256));
        PackageSize = packageSize;

        var bindingFacts = bindings
            .OrderBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DeviceId)
            .ToArray();
        var pluginFacts = plugins
            .OrderBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (bindingFacts.Length == 0 || pluginFacts.Length == 0)
        {
            throw new ArgumentException("Installer generation facts cannot be empty.");
        }

        BindingsJson = JsonSerializer.Serialize(bindingFacts);
        PluginsJson = JsonSerializer.Serialize(pluginFacts);
    }

    public Guid? OperatorUserId { get; private set; }

    public string? OperatorName { get; private set; }

    public DateTime GeneratedAtUtc { get; private set; }

    public string Channel { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

    public string HostVersion { get; private set; } = null!;

    public string HostSha256 { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string PackageSha256 { get; private set; } = null!;

    public long PackageSize { get; private set; }

    public string BindingsJson { get; private set; } = "[]";

    public string PluginsJson { get; private set; } = "[]";

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeSha256(string value, string parameterName)
    {
        var normalized = NormalizeRequired(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Value must be a SHA256 hash.", parameterName);
        }

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}

public sealed record EdgeInstallerGenerationBindingFact(
    string ModuleId,
    Guid DeviceId,
    string ClientCode,
    string DeviceName,
    Guid ProcessId);

public sealed record EdgeInstallerGenerationPluginFact(
    string ModuleId,
    string Version,
    string Sha256);
