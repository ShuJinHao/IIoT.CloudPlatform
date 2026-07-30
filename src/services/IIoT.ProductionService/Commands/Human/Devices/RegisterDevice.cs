using System.Security.Cryptography;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Create")]
[AdminOnly]
[DistributedLock("iiot:lock:device-create:{DeviceName}", TimeoutSeconds = 5)]
public record RegisterDeviceCommand(
    string DeviceName,
    Guid ProcessId
) : IHumanCommand<Result<CreateDeviceResultDto>>;

public sealed record CreateDeviceResultDto(
    Guid Id,
    string Code);

public class RegisterDeviceHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceReadQueryService deviceReadQueryService,
    IAuditTrailService auditTrailService,
    IUnitOfWork unitOfWork,
    IDeviceWriteObservationReader observationReader,
    IDeviceClientStateStore clientStateStore
) : ICommandHandler<RegisterDeviceCommand, Result<CreateDeviceResultDto>>
{
    public async Task<Result<CreateDeviceResultDto>> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserDeviceAccessService.IsAdministrator)
            return await FailAsync(request, "只有管理员可以注册设备", cancellationToken);

        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return await FailAsync(request, "设备名称不能为空", cancellationToken);
        if (request.ProcessId == Guid.Empty)
            return await FailAsync(request, "工序不能为空", cancellationToken);

        var processExists = await processReadQueryService.ExistsAsync(
            request.ProcessId,
            cancellationToken);
        if (!processExists)
            return await FailAsync(request, "设备注册失败：指定工序不存在", cancellationToken);

        if (await deviceReadQueryService.NameExistsAsync(
                deviceName,
                cancellationToken: cancellationToken))
        {
            return await FailAsync(
                request,
                "设备注册失败：设备名称已存在，请换一个名称",
                cancellationToken);
        }

        var code = await GenerateUniqueCodeAsync(
            deviceReadQueryService,
            cancellationToken);
        if (code is null)
            return await FailAsync(request, "设备注册失败：无法生成唯一设备寻址码", cancellationToken);

        var deviceId = Guid.NewGuid();
        var clientStateId = Guid.NewGuid();
        var auditExecutedAtUtc = DateTime.UtcNow;
        var writeAttempted = false;
        var commitAttempted = false;
        var commitRecovered = false;
        uint? targetRowVersion = null;
        Result<CreateDeviceResultDto> result;
        try
        {
            result = await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested
                  && !commitAttempted)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch (Exception) when (commitAttempted)
        {
            result = await ResolveCommitAsync();
            commitRecovered = true;
        }

        if (result.IsSuccess)
        {
            var auditEntry = new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Register",
                "Device",
                deviceId.ToString(),
                auditExecutedAtUtc,
                true,
                $"注册设备 {deviceName}（{code}）到工序 {request.ProcessId}。",
                IdempotencyKey: $"device-register:{deviceId:N}");
            if (commitRecovered)
            {
                await CloudWriteCommitRecovery.ConfirmRecoveredAuditAsync(
                    auditTrailService,
                    auditEntry);
            }
            else
            {
                await auditTrailService.TryWriteAsync(
                    auditEntry,
                    cancellationToken);
            }
        }

        return result;

        async Task<Result<CreateDeviceResultDto>> ExecuteTransactionAsync(
            CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            if (MatchesTarget(current))
            {
                return Success();
            }

            if (current.Target is not null
                || current.DeviceNameOwnerId is not null
                || current.ClientCodeOwnerId is not null
                || !current.ProcessExists)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return !current.ProcessExists
                    ? Result.Failure(
                        "设备注册失败：指定工序不存在")
                    : Result.Failure(
                        "设备注册失败：设备名称或寻址码已被占用");
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var device = new Device(
                deviceId,
                deviceName,
                code,
                request.ProcessId);
            deviceRepository.Add(device);
            clientStateStore.AddState(
                new DeviceClientState(
                    deviceId,
                    code,
                    clientStateId,
                    auditExecutedAtUtc));
            await deviceRepository.SaveChangesAsync(callbackToken);
            targetRowVersion = device.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Success();
        }

        async Task<Result<CreateDeviceResultDto>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveDeviceAsync(
                    deviceId,
                    deviceName,
                    code,
                    request.ProcessId,
                    token));
            if (current is null
                || current.Target is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current))
            {
                return Success();
            }

            throw new CloudWriteConflictException();
        }

        async Task<DeviceWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveDeviceAsync(
                    deviceId,
                    deviceName,
                    code,
                    request.ProcessId,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }

        bool MatchesTarget(DeviceWriteObservation current)
            => current.Target is not null
               && current.Target.Id == deviceId
               && string.Equals(
                   current.Target.DeviceName,
                   deviceName,
                   StringComparison.Ordinal)
               && string.Equals(
                   current.Target.ClientCode,
                   code,
                   StringComparison.Ordinal)
               && current.Target.ProcessId == request.ProcessId
               && (!targetRowVersion.HasValue
                   || current.Target.RowVersion == targetRowVersion.Value)
               && current.DeviceNameOwnerId == deviceId
               && current.ClientCodeOwnerId == deviceId;

        Result<CreateDeviceResultDto> Success()
            => Result.Success(new CreateDeviceResultDto(deviceId, code));
    }

    private async Task<Result<CreateDeviceResultDto>> FailAsync(
        RegisterDeviceCommand request,
        string message,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Register",
                "Device",
                $"{request.ProcessId}:{request.DeviceName?.Trim()}",
                DateTime.UtcNow,
                false,
                $"注册设备 {request.DeviceName?.Trim()}。",
                message),
            cancellationToken);

        return Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;

    private static async Task<string?> GenerateUniqueCodeAsync(
        IDeviceReadQueryService deviceReadQueryService,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = DeviceCodeGenerator.Generate();
            if (!await deviceReadQueryService.CodeExistsAsync(
                    candidate,
                    cancellationToken: cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }
}

internal static class DeviceCodeGenerator
{
    private const string Prefix = "DEV-";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int RandomPartLength = 10;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[RandomPartLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Concat(Prefix, new string(chars));
    }
}
