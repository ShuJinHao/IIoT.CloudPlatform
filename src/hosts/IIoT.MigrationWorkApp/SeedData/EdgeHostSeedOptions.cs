using Microsoft.Extensions.Configuration;

namespace IIoT.MigrationWorkApp.SeedData;

public sealed record EdgeHostSeedOptions(
    bool Enabled,
    IReadOnlyList<EdgeHostSeedHostOptions> Hosts)
{
    public const string SectionName = "EdgeHostSeeds";
    public const string EnabledKey = "Enabled";
    public const string HostsKey = "Hosts";

    public static EdgeHostSeedOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new EdgeHostSeedOptions(
            IsEnabled(section[EnabledKey]),
            section.GetSection(HostsKey)
                .GetChildren()
                .Select(EdgeHostSeedHostOptions.From)
                .ToList());

        if (options.Enabled)
        {
            Validate(options.Hosts);
        }

        return options;
    }

    private static void Validate(IReadOnlyList<EdgeHostSeedHostOptions> hosts)
    {
        var hostCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
        {
            Require(host.ClientCode, "EdgeHostSeeds:Hosts:ClientCode");
            Require(host.HostName, "EdgeHostSeeds:Hosts:HostName");

            if (!hostCodes.Add(host.ClientCode!.Trim()))
            {
                throw new InvalidOperationException(
                    $"EdgeHost seed 配置包含重复 ClientCode [{host.ClientCode}]。");
            }

            var plcCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plc in host.PlcBindings)
            {
                Require(plc.PlcCode, $"EdgeHostSeeds:Hosts[{host.ClientCode}]:PlcBindings:PlcCode");
                Require(plc.PlcName, $"EdgeHostSeeds:Hosts[{host.ClientCode}]:PlcBindings:PlcName");
                _ = plc.ResolveDisplayOrder();

                if (!plcCodes.Add(plc.PlcCode!.Trim()))
                {
                    throw new InvalidOperationException(
                        $"EdgeHost seed 配置中 ClientCode [{host.ClientCode}] 包含重复 PLC [{plc.PlcCode}]。");
                }
            }
        }
    }

    private static void Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required EdgeHost seed configuration '{key}'.");
        }
    }

    internal static bool IsEnabled(string? value, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record EdgeHostSeedHostOptions(
    string? ClientCode,
    string? HostName,
    string? Remark,
    bool Enabled,
    bool UpdateExisting,
    bool AddMissingBindingsToExistingHost,
    bool UpdateExistingBindings,
    IReadOnlyList<EdgeHostSeedPlcBindingOptions> PlcBindings)
{
    public static EdgeHostSeedHostOptions From(IConfigurationSection section)
    {
        return new EdgeHostSeedHostOptions(
            section["ClientCode"],
            section["HostName"],
            section["Remark"],
            EdgeHostSeedOptions.IsEnabled(section["Enabled"], defaultValue: true),
            EdgeHostSeedOptions.IsEnabled(section["UpdateExisting"]),
            EdgeHostSeedOptions.IsEnabled(section["AddMissingBindingsToExistingHost"]),
            EdgeHostSeedOptions.IsEnabled(section["UpdateExistingBindings"]),
            section.GetSection("PlcBindings")
                .GetChildren()
                .Select(EdgeHostSeedPlcBindingOptions.From)
                .ToList());
    }
}

public sealed record EdgeHostSeedPlcBindingOptions(
    string? PlcCode,
    string? PlcName,
    string? ProcessCode,
    string? BusinessDeviceCode,
    string? StationCode,
    string? Protocol,
    string? Address,
    string? DisplayOrder,
    string? Remark,
    bool Enabled,
    bool UpdateExisting)
{
    public static EdgeHostSeedPlcBindingOptions From(IConfigurationSection section)
    {
        return new EdgeHostSeedPlcBindingOptions(
            section["PlcCode"],
            section["PlcName"],
            section["ProcessCode"],
            section["BusinessDeviceCode"],
            section["StationCode"],
            section["Protocol"],
            section["Address"],
            section["DisplayOrder"],
            section["Remark"],
            EdgeHostSeedOptions.IsEnabled(section["Enabled"], defaultValue: true),
            EdgeHostSeedOptions.IsEnabled(section["UpdateExisting"]));
    }

    public int ResolveDisplayOrder()
    {
        if (string.IsNullOrWhiteSpace(DisplayOrder))
        {
            return 0;
        }

        return int.TryParse(DisplayOrder, out var order)
            ? order
            : throw new InvalidOperationException(
                $"EdgeHost seed PLC [{PlcCode}] DisplayOrder 必须是整数。");
    }
}
