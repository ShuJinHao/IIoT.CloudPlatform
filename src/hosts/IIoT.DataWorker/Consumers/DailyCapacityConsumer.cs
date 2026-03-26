using IIoT.Core.Production.Aggregates.Capacities;
using IIoT.EntityFrameworkCore;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIoT.DataWorker.Consumers;

public class DailyCapacityConsumer(
    IIoTDbContext dbContext,
    ICacheService cacheService,
    ILogger<DailyCapacityConsumer> logger)
    : IConsumer<DailyCapacityReceivedEvent>
{
    public async Task Consume(ConsumeContext<DailyCapacityReceivedEvent> context)
    {
        var message = context.Message;
        logger.LogInformation("接收到产能汇总: DeviceId={DeviceId}, Date={Date}, Shift={ShiftCode}",
            message.DeviceId, message.Date, message.ShiftCode);

        // 1. 校验设备是否存在且激活
        var deviceExists = await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(d => d.Id == message.DeviceId && d.IsActive);

        if (!deviceExists)
        {
            logger.LogWarning("设备 {DeviceId} 不存在或已停用，跳过写入。", message.DeviceId);
            return;
        }

        // 2. Upsert 语义：同一设备同一天同一班次，已存在则覆盖更新
        var existing = await dbContext.DailyCapacities
            .FirstOrDefaultAsync(c => c.DeviceId == message.DeviceId
                                   && c.Date == message.Date
                                   && c.ShiftCode == message.ShiftCode);

        if (existing != null)
        {
            existing.UpdateCapacity(message.TotalCount, message.OkCount, message.NgCount);
            logger.LogInformation("产能汇总已存在，执行覆盖更新。");
        }
        else
        {
            var record = new DailyCapacity(
                message.DeviceId,
                message.Date,
                message.ShiftCode,
                message.TotalCount,
                message.OkCount,
                message.NgCount);

            dbContext.DailyCapacities.Add(record);
        }

        await dbContext.SaveChangesAsync();

        // 3. 缓存爆破：单机台产能缓存失效
        await cacheService.RemoveAsync($"iiot:capacity:summary:v1:{message.DeviceId}");

        logger.LogInformation("产能汇总写入成功: DeviceId={DeviceId}, Date={Date}", message.DeviceId, message.Date);
    }
}