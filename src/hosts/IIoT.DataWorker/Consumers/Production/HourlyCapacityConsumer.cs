using IIoT.ProductionService.Commands.Capacities;
using IIoT.Services.Contracts.Events.Capacities;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 半小时产能事件消费者。
/// 把事件转成内部落库命令；命令失败时抛异常，让 MassTransit 接管重试和错误队列。
/// </summary>
public sealed class HourlyCapacityConsumer(ISender sender)
    : IConsumer<HourlyCapacityReceivedEvent>
{
    public async Task Consume(ConsumeContext<HourlyCapacityReceivedEvent> context)
    {
        var command = new PersistHourlyCapacityCommand(context.Message);
        var result = await sender.Send(command, context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"半小时产能落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
