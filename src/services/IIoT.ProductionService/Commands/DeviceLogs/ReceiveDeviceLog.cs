using IIoT.Services.Common.Events;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using MassTransit;

namespace IIoT.ProductionService.Commands.DeviceLogs;

/// <summary>
/// 业务指令：接收设备日志（支持批量）
/// 边缘端 LogPushTask 定时批量推送
/// </summary>
public record ReceiveDeviceLogCommand(
    List<DeviceLogItem> Logs
) : ICommand<Result<bool>>;

public class ReceiveDeviceLogHandler(
    IPublishEndpoint publishEndpoint
) : ICommandHandler<ReceiveDeviceLogCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ReceiveDeviceLogCommand request, CancellationToken cancellationToken)
    {
        if (request.Logs.Count == 0)
            return Result.Failure("数据接收失败：日志列表不能为空");

        // 直接发布事件到 MQ，DeviceLogItem 在 Event 里已经定义
        var @event = new DeviceLogReceivedEvent
        {
            Logs = request.Logs
        };

        await publishEndpoint.Publish(@event, cancellationToken);

        return Result.Success(true);
    }
}