using System.Text.Json;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement(DevicePermissions.MigrateProcess)]
[AdminOnly]
[DistributedLock("iiot:lock:device-write:{DeviceId}", TimeoutSeconds = 5)]
public sealed record MigrateDeviceProcessCommand(
    Guid DeviceId,
    Guid ExpectedSourceProcessId,
    Guid TargetProcessId,
    uint ExpectedRowVersion,
    string ConfirmationText)
    : IHumanCommand<Result<DeviceProcessMigrationResultDto>>,
        IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "Device.Process.Migrate";

    public string AdminAuditTargetType => "Device";

    public string AdminAuditTargetIdOrKey => DeviceId.ToString();
}

public sealed record DeviceProcessMigrationResultDto(
    Guid DeviceId,
    Guid SourceProcessId,
    Guid TargetProcessId,
    uint RowVersion);

public sealed class MigrateDeviceProcessHandler(
    ICurrentUser currentUser,
    IReadRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceDeletionDependencyQueryService migrationService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IAuditTrailService auditTrailService)
    : ICommandHandler<
        MigrateDeviceProcessCommand,
        Result<DeviceProcessMigrationResultDto>>
{
    public async Task<Result<DeviceProcessMigrationResultDto>> Handle(
        MigrateDeviceProcessCommand request,
        CancellationToken cancellationToken)
    {
        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (device is null)
        {
            return await FailAsync(
                request,
                "目标设备不存在。",
                null,
                cancellationToken);
        }

        var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return await FailAsync(
                request,
                access.Errors?.FirstOrDefault()
                ?? "越权：未授权访问该设备。",
                device,
                cancellationToken);
        }

        if (request.ExpectedSourceProcessId == Guid.Empty
            || request.TargetProcessId == Guid.Empty)
        {
            return await FailAsync(
                request,
                "源工序和目标工序不能为空。",
                device,
                cancellationToken);
        }

        if (device.ProcessId != request.ExpectedSourceProcessId
            || device.RowVersion != request.ExpectedRowVersion)
        {
            throw new CloudWriteConflictException();
        }

        var processes = await processReadQueryService.GetByIdsAsync(
            [device.ProcessId, request.TargetProcessId],
            cancellationToken);
        var source = processes.SingleOrDefault(
            process => process.Id == device.ProcessId);
        var target = processes.SingleOrDefault(
            process => process.Id == request.TargetProcessId);
        if (source is null || target is null)
        {
            return await FailAsync(
                request,
                target is null ? "目标工序不存在。" : "设备当前工序主数据不存在。",
                device,
                cancellationToken);
        }

        var requiredConfirmation =
            DeviceProcessMigrationPolicy.BuildConfirmationText(
                device.Code,
                target.ProcessCode);
        if (!string.Equals(
                request.ConfirmationText?.Trim(),
                requiredConfirmation,
                StringComparison.Ordinal))
        {
            return await FailAsync(
                request,
                "迁移确认文本不匹配。",
                device,
                cancellationToken);
        }

        var migratedAtUtc = DateTime.UtcNow;
        var result = await migrationService.MigrateProcessAsync(
            device.Id,
            request.ExpectedSourceProcessId,
            request.TargetProcessId,
            request.ExpectedRowVersion,
            new DeviceProcessMigrationAuditContext(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                migratedAtUtc),
            cancellationToken);
        if (!result.Migrated || !result.RowVersion.HasValue)
        {
            var message = result.Status switch
            {
                DeviceProcessMigrationStatus.DeviceNotFound => "目标设备不存在。",
                DeviceProcessMigrationStatus.TargetProcessNotFound => "目标工序不存在。",
                DeviceProcessMigrationStatus.SameProcess => "设备已经属于目标工序。",
                DeviceProcessMigrationStatus.Blocked =>
                    "设备存在带工序语义的关联数据，禁止迁移。",
                _ => "设备工序迁移未完成。"
            };
            return await FailAsync(
                request,
                message,
                device,
                cancellationToken);
        }

        return Result.Success(new DeviceProcessMigrationResultDto(
            device.Id,
            request.ExpectedSourceProcessId,
            request.TargetProcessId,
            result.RowVersion.Value));
    }

    private async Task<Result<DeviceProcessMigrationResultDto>> FailAsync(
        MigrateDeviceProcessCommand request,
        string message,
        Device? device,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Process.Migrate",
                "Device",
                request.DeviceId.ToString(),
                DateTime.UtcNow,
                false,
                JsonSerializer.Serialize(new
                {
                    action = "DeviceProcessMigration",
                    deviceId = request.DeviceId,
                    clientCode = device?.Code,
                    expectedSourceProcessId = request.ExpectedSourceProcessId,
                    targetProcessId = request.TargetProcessId,
                    expectedRowVersion = request.ExpectedRowVersion
                }),
                message),
            cancellationToken);
        return Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
}
