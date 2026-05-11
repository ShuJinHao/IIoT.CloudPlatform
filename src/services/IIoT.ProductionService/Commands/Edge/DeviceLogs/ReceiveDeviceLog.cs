using AutoMapper;
using IIoT.ProductionService.Commands;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Uploads;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.DeviceLogs;

public record ReceiveDeviceLogCommand(
    Guid DeviceId,
    List<DeviceLogItem> Logs,
    string? RequestId = null
) : IDeviceCommand<Result<EdgeUploadAcceptedResponse>>;

public class ReceiveDeviceLogHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IMapper mapper,
    IUploadReceiveRegistry uploadReceiveRegistry
) : ICommandHandler<ReceiveDeviceLogCommand, Result<EdgeUploadAcceptedResponse>>
{
    public async Task<Result<EdgeUploadAcceptedResponse>> Handle(
        ReceiveDeviceLogCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        if (request.Logs is null || request.Logs.Count == 0)
            return Result.Failure("数据接收失败: 日志列表不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(
            request.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败: 设备不存在");

        var deduplicationKey = UploadDeduplicationKeys.ForDeviceLog(request);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var @event = mapper.Map<DeviceLogReceivedEvent>(request);
        var registration = await uploadReceiveRegistry.RegisterAndEnqueueAsync(
            request.DeviceId,
            UploadMessageTypes.DeviceLog,
            UploadDeduplicationKeys.NormalizeRequestId(request.RequestId),
            deduplicationKey.Value!,
            @event,
            cancellationToken);

        return Result.Success(registration.IsDuplicate
            ? EdgeUploadAcceptedResponse.Duplicate(registration.OutboxMessageId)
            : EdgeUploadAcceptedResponse.Accepted(registration.OutboxMessageId));
    }
}
