using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Update")]
[DistributedLock("iiot:lock:device-write:{DeviceId}", TimeoutSeconds = 5)]
public record UpdateDeviceProfileCommand(
    Guid DeviceId,
    string DeviceName
) : IHumanCommand<Result<bool>>;

public class UpdateDeviceProfileHandler(
    IRepository<Device> deviceRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IUnitOfWork unitOfWork,
    IDeviceWriteObservationReader observationReader)
    : ICommandHandler<UpdateDeviceProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateDeviceProfileCommand request,
        CancellationToken cancellationToken)
    {
        var deviceName = request.DeviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(deviceName))
            return Result.Failure("设备名称不能为空");

        var accessTarget = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (accessTarget is null)
            return Result.Failure("目标设备不存在");

        var deviceAccess =
            await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                accessTarget.Id,
                cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(
                deviceAccess.Errors?.ToArray()
                ?? ["越权:未授权访问该设备"]);
        }

        var clientCode = accessTarget.Code;
        var processId = accessTarget.ProcessId;
        DeviceWriteState? baseline = null;
        uint? targetRowVersion = null;
        var writeAttempted = false;
        var commitAttempted = false;
        try
        {
            return await unitOfWork.ExecuteResilientAsync(
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
            return await ResolveCommitAsync();
        }

        async Task<Result<bool>> ExecuteTransactionAsync(
            CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            if (MatchesTarget(current.Target))
            {
                return Result.Success(true);
            }

            if (current.Target is null)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure("目标设备不存在");
            }

            if (current.DeviceNameOwnerId is Guid ownerId
                && ownerId != request.DeviceId)
            {
                if (writeAttempted)
                {
                    throw new CloudWriteConflictException();
                }

                return Result.Failure("设备名称已存在，请换一个名称");
            }

            baseline ??= current.Target;
            if (current.Target != baseline)
            {
                throw new CloudWriteConflictException();
            }

            writeAttempted = true;
            await unitOfWork.BeginTransactionAsync(callbackToken);
            var device = await deviceRepository.GetSingleOrDefaultAsync(
                new DeviceByIdSpec(request.DeviceId),
                callbackToken);
            if (device is null
                || device.RowVersion != baseline.RowVersion)
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            device.Rename(deviceName);
            deviceRepository.Update(device);
            await deviceRepository.SaveChangesAsync(callbackToken);
            targetRowVersion = device.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Result.Success(true);
        }

        async Task<Result<bool>> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveDeviceAsync(
                    request.DeviceId,
                    deviceName,
                    clientCode,
                    processId,
                    token));
            if (current is null
                || baseline is null
                || current.Target == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(current.Target))
            {
                return Result.Success(true);
            }

            throw new CloudWriteConflictException();
        }

        async Task<DeviceWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveDeviceAsync(
                    request.DeviceId,
                    deviceName,
                    clientCode,
                    processId,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }

        bool MatchesTarget(DeviceWriteState? state)
            => state is not null
               && targetRowVersion.HasValue
               && state.Id == request.DeviceId
               && state.RowVersion == targetRowVersion.Value
               && string.Equals(
                   state.DeviceName,
                   deviceName,
                   StringComparison.Ordinal)
               && string.Equals(
                   state.ClientCode,
                   clientCode,
                   StringComparison.Ordinal)
               && state.ProcessId == processId;
    }
}
