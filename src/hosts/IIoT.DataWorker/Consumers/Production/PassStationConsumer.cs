using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Common.Events.PassStations;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 泛型过站数据入库消费者。
/// Handler 成功返回后确认消息；抛异常时交给 MassTransit 重试。
/// </summary>
public sealed class PassStationConsumer<TEvent>(ISender sender)
    : IConsumer<TEvent>
    where TEvent : class, IPassStationEvent
{
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var result = await sender.Send(
            new PersistPassStationCommand<TEvent>(context.Message),
            context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"过站数据批量落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
