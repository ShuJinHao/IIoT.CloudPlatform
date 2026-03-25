using IIoT.Application.Contracts;
using IIoT.Services.Common.Events;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using AutoMapper;
using MassTransit;

namespace IIoT.ProductionService.Commands.Capacities;

/// <summary>
/// 业务指令：接收每日产能汇总
/// 边缘端 DailyReportTask 每天定时上报
/// </summary>
public record ReceiveDailyCapacityCommand(
    Guid DeviceId,
    DateOnly Date,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount
) : ICommand<Result<bool>>;

public class ReceiveDailyCapacityHandler(
    IDataQueryService dataQueryService,
    IMapper mapper,
    IPublishEndpoint publishEndpoint
) : ICommandHandler<ReceiveDailyCapacityCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ReceiveDailyCapacityCommand request, CancellationToken cancellationToken)
    {
        // 1. 校验设备是否存在且激活
        var deviceExists = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d => d.Id == request.DeviceId && d.IsActive)
        );

        if (!deviceExists)
            return Result.Failure("数据接收失败：设备不存在或已停用");

        // 2. 转换为事件并发布到 MQ
        var @event = mapper.Map<DailyCapacityReceivedEvent>(request);
        await publishEndpoint.Publish(@event, cancellationToken);

        return Result.Success(true);
    }
}