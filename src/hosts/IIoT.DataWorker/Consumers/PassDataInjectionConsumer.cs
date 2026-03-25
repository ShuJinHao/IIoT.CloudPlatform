using IIoT.Core.Production.Aggregates.PassStations;
using IIoT.EntityFrameworkCore;
using IIoT.Services.Common.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIoT.DataWorker.Consumers;

public class PassDataInjectionConsumer(
    IIoTDbContext dbContext,
    ILogger<PassDataInjectionConsumer> logger)
    : IConsumer<PassDataInjectionReceivedEvent>
{
    public async Task Consume(ConsumeContext<PassDataInjectionReceivedEvent> context)
    {
        var message = context.Message;
        logger.LogInformation("接收到注液过站数据: DeviceId={DeviceId}, Barcode={Barcode}",
            message.DeviceId, message.Barcode);

        // 1. 校验设备是否存在且激活
        var deviceExists = await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(d => d.Id == message.DeviceId && d.IsActive);

        if (!deviceExists)
        {
            logger.LogWarning("设备 {DeviceId} 不存在或已停用，跳过写入。", message.DeviceId);
            return;
        }

        // 2. 幂等性校验：同一设备同一条码同一完成时间不重复写入
        var duplicate = await dbContext.PassDataInjection
            .AsNoTracking()
            .AnyAsync(p => p.DeviceId == message.DeviceId
                        && p.Barcode == message.Barcode
                        && p.CompletedTime == message.CompletedTime);

        if (duplicate)
        {
            logger.LogInformation("注液过站数据已存在，跳过重复写入: Barcode={Barcode}", message.Barcode);
            return;
        }

        // 3. 构建领域实体并持久化
        var record = new PassDataInjection(
            message.DeviceId,
            message.CellResult,
            message.CompletedTime,
            message.Barcode,
            message.PreInjectionTime,
            message.PreInjectionWeight,
            message.PostInjectionTime,
            message.PostInjectionWeight,
            message.InjectionVolume);

        dbContext.PassDataInjection.Add(record);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("注液过站数据写入成功: Id={Id}, Barcode={Barcode}", record.Id, record.Barcode);
    }
}