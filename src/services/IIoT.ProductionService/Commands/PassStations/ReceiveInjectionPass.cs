using IIoT.Application.Contracts;
using IIoT.Services.Common.Events;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using AutoMapper;
using MassTransit;

namespace IIoT.ProductionService.Commands.PassStations;

/// <summary>
/// 业务指令：接收注液工序过站数据
/// 边缘端 CloudConsumer 上报，校验通过后发布事件到 MQ，DataWorker 异步写库
/// </summary>
public record ReceiveInjectionPassCommand(
    Guid DeviceId,
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume
) : ICommand<Result<bool>>;

public class ReceiveInjectionPassHandler(
    IDataQueryService dataQueryService,
    IMapper mapper,
    IPublishEndpoint publishEndpoint
) : ICommandHandler<ReceiveInjectionPassCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ReceiveInjectionPassCommand request, CancellationToken cancellationToken)
    {
        // 1. 校验设备是否存在且激活
        var deviceExists = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d => d.Id == request.DeviceId && d.IsActive)
        );

        if (!deviceExists)
            return Result.Failure("数据接收失败：设备不存在或已停用");

        // 2. 转换为事件并发布到 MQ
        var @event = mapper.Map<PassDataInjectionReceivedEvent>(request);
        await publishEndpoint.Publish(@event, cancellationToken);

        return Result.Success(true);
    }
}