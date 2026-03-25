using IIoT.Core.Production.Aggregates;
using IIoT.EntityFrameworkCore;
using IIoT.Services.Common.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace IIoT.DataWorker.Consumers;

public class DeviceLogConsumer(
    IIoTDbContext dbContext,
    ILogger<DeviceLogConsumer> logger)
    : IConsumer<DeviceLogReceivedEvent>
{
    public async Task Consume(ConsumeContext<DeviceLogReceivedEvent> context)
    {
        var message = context.Message;
        logger.LogInformation("接收到设备日志批量推送，共 {Count} 条。", message.Logs.Count);

        if (message.Logs.Count == 0) return;

        var entities = message.Logs.Select(log => new DeviceLog(
            log.DeviceId,
            log.Level,
            log.Message,
            log.LogTime
        )).ToList();

        dbContext.DeviceLogs.AddRange(entities);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("设备日志批量写入成功，共 {Count} 条。", entities.Count);
    }
}