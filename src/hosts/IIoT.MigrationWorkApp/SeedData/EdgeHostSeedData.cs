using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Devices.ValueObjects;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IIoT.MigrationWorkApp.SeedData;

public static class EdgeHostSeedData
{
    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var options = EdgeHostSeedOptions.Load(configuration);
        if (!options.Enabled)
        {
            Console.WriteLine("EdgeHost seed is disabled. Skip EdgeHost/PLC business data seeding.");
            return;
        }

        if (options.Hosts.Count == 0)
        {
            Console.WriteLine("EdgeHost seed is enabled but no hosts are configured.");
            return;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var summary = new EdgeHostSeedSummary();
                foreach (var hostSeed in options.Hosts)
                {
                    await ApplyHostSeedAsync(dbContext, hostSeed, summary, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                Console.WriteLine(
                    "EdgeHost seed completed. hosts_created={0}, hosts_updated={1}, plc_bindings_created={2}, plc_bindings_updated={3}, plc_bindings_skipped={4}.",
                    summary.CreatedHosts,
                    summary.UpdatedHosts,
                    summary.CreatedBindings,
                    summary.UpdatedBindings,
                    summary.SkippedBindings);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private static async Task ApplyHostSeedAsync(
        IIoTDbContext dbContext,
        EdgeHostSeedHostOptions hostSeed,
        EdgeHostSeedSummary summary,
        CancellationToken cancellationToken)
    {
        var clientCode = DeviceCode.From(hostSeed.ClientCode!).Value;
        var device = await dbContext.Devices
            .SingleOrDefaultAsync(item => item.Code == clientCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"EdgeHost seed failed: Device with ClientCode [{clientCode}] does not exist.");

        var host = await dbContext.EdgeHosts
            .Include(item => item.PlcBindings)
            .SingleOrDefaultAsync(item => item.ClientCode == clientCode, cancellationToken);

        var createdHost = false;
        if (host is null)
        {
            host = new EdgeHost(device.Id, clientCode, hostSeed.HostName!, hostSeed.Remark);
            ApplyHostEnabled(host, hostSeed.Enabled);
            createdHost = true;
            summary.CreatedHosts++;
        }
        else
        {
            if (host.DeviceId != device.Id)
            {
                throw new InvalidOperationException(
                    $"EdgeHost seed failed: existing EdgeHost [{clientCode}] is bound to another DeviceId.");
            }

            if (hostSeed.UpdateExisting)
            {
                host.Rename(hostSeed.HostName!);
                host.UpdateRemark(hostSeed.Remark);
                ApplyHostEnabled(host, hostSeed.Enabled);
                summary.UpdatedHosts++;
            }
        }

        var canAddMissingBindings = createdHost || hostSeed.AddMissingBindingsToExistingHost;
        foreach (var plcSeed in hostSeed.PlcBindings)
        {
            await ApplyPlcBindingSeedAsync(
                dbContext,
                host,
                plcSeed,
                canAddMissingBindings,
                hostSeed.UpdateExistingBindings,
                summary,
                cancellationToken);
        }

        if (createdHost)
        {
            dbContext.EdgeHosts.Add(host);
        }
    }

    private static async Task ApplyPlcBindingSeedAsync(
        IIoTDbContext dbContext,
        EdgeHost host,
        EdgeHostSeedPlcBindingOptions plcSeed,
        bool canAddMissingBindings,
        bool updateExistingBindings,
        EdgeHostSeedSummary summary,
        CancellationToken cancellationToken)
    {
        var plcCode = NormalizeCode(plcSeed.PlcCode!);
        var binding = host.FindPlcBinding(plcSeed.PlcCode!);
        if (binding is null && !canAddMissingBindings)
        {
            summary.SkippedBindings++;
            return;
        }

        if (binding is not null && !updateExistingBindings && !plcSeed.UpdateExisting)
        {
            summary.SkippedBindings++;
            return;
        }

        var processId = await ResolveProcessIdAsync(dbContext, plcSeed.ProcessCode, cancellationToken);
        var businessDevice = await ResolveBusinessDeviceAsync(
            dbContext,
            plcSeed.BusinessDeviceCode,
            cancellationToken);

        if (processId.HasValue
            && businessDevice is not null
            && businessDevice.ProcessId != processId.Value)
        {
            throw new InvalidOperationException(
                $"EdgeHost seed failed: PLC [{plcCode}] business device [{businessDevice.Code}] does not belong to process [{plcSeed.ProcessCode}].");
        }

        if (binding is null)
        {
            host.AddPlcBinding(
                plcSeed.PlcCode!,
                plcSeed.PlcName!,
                processId,
                businessDevice?.Id,
                plcSeed.StationCode,
                plcSeed.Protocol,
                plcSeed.Address,
                plcSeed.ResolveDisplayOrder(),
                plcSeed.Remark,
                plcSeed.Enabled);
            summary.CreatedBindings++;
            return;
        }

        host.UpdatePlcBinding(
            binding.Id,
            plcSeed.PlcName!,
            processId,
            businessDevice?.Id,
            plcSeed.StationCode,
            plcSeed.Protocol,
            plcSeed.Address,
            plcSeed.ResolveDisplayOrder(),
            plcSeed.Remark);
        ApplyBindingEnabled(host, binding.Id, plcSeed.Enabled);
        summary.UpdatedBindings++;
    }

    private static async Task<Guid?> ResolveProcessIdAsync(
        IIoTDbContext dbContext,
        string? processCode,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeOptional(processCode);
        if (normalized is null)
        {
            return null;
        }

        var process = await dbContext.MfgProcesses
            .SingleOrDefaultAsync(item => item.ProcessCode == normalized, cancellationToken);
        return process?.Id
            ?? throw new InvalidOperationException(
                $"EdgeHost seed failed: MfgProcess with ProcessCode [{normalized}] does not exist.");
    }

    private static async Task<Device?> ResolveBusinessDeviceAsync(
        IIoTDbContext dbContext,
        string? businessDeviceCode,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeOptional(businessDeviceCode);
        if (normalized is null)
        {
            return null;
        }

        var deviceCode = DeviceCode.From(normalized).Value;
        return await dbContext.Devices
            .SingleOrDefaultAsync(item => item.Code == deviceCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"EdgeHost seed failed: business Device with ClientCode [{deviceCode}] does not exist.");
    }

    private static void ApplyHostEnabled(EdgeHost host, bool enabled)
    {
        if (enabled)
        {
            host.Enable();
            return;
        }

        host.Disable();
    }

    private static void ApplyBindingEnabled(EdgeHost host, Guid bindingId, bool enabled)
    {
        if (enabled)
        {
            host.EnablePlcBinding(bindingId);
            return;
        }

        host.DisablePlcBinding(bindingId);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeCode(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private sealed class EdgeHostSeedSummary
    {
        public int CreatedHosts { get; set; }
        public int UpdatedHosts { get; set; }
        public int CreatedBindings { get; set; }
        public int UpdatedBindings { get; set; }
        public int SkippedBindings { get; set; }
    }
}
