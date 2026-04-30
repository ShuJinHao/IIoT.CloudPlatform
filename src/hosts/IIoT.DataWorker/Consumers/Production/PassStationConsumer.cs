using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Contracts.Events.PassStations;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 泛型过站事件消费者。
/// 支持不同过站事件共用同一套消费外壳，真正的映射和落库逻辑由内部命令链路完成。
/// </summary>
public sealed class PassStationConsumer(ISender sender)
    : IConsumer<PassStationBatchReceivedEvent>
{
    public async Task Consume(ConsumeContext<PassStationBatchReceivedEvent> context)
    {
        EventSchemaVersionGuard.EnsureSupported(
            context.Message.SchemaVersion,
            nameof(PassStationBatchReceivedEvent));

        var result = await sender.Send(
            new PersistPassStationCommand(context.Message),
            context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"过站数据批量落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
