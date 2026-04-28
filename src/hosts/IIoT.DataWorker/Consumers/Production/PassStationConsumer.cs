using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Contracts.Events.PassStations;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 泛型过站事件消费者。
/// 支持不同过站事件共用同一套消费外壳，真正的映射和落库逻辑由内部命令链路完成。
/// </summary>
public sealed class PassStationConsumer<TEvent>(ISender sender)
    : IConsumer<TEvent>
    where TEvent : class, IPassStationEvent
{
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        EventSchemaVersionGuard.EnsureSupported(
            context.Message.SchemaVersion,
            typeof(TEvent).Name);

        var result = await sender.Send(
            new PersistPassStationCommand<TEvent>(context.Message),
            context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"过站数据批量落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
