using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Events;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using AutoMapper;
using MassTransit;

namespace IIoT.ProductionService.Commands.PassStations;

/// <summary>
/// 业务指令:接收注液工序过站数据(单条)。
/// 上位机推送到 HttpApi → 校验设备存在且活跃 → 发布到 MQ → DataWorker 消费落库。
/// </summary>
public record ReceiveInjectionPassCommand(
    Guid DeviceId,
    string MacAddress,
    string ClientCode,
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
    public async Task<Result<bool>> Handle(
        ReceiveInjectionPassCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MacAddress) || string.IsNullOrWhiteSpace(request.ClientCode))
            return Result.Failure("数据接收失败:身份信息不完整(MacAddress + ClientCode 必填)");

        var deviceExists = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d => d.Id == request.DeviceId && d.IsActive));

        if (!deviceExists)
            return Result.Failure("数据接收失败:设备不存在或已停用");

        // 边缘端时间戳统一转 UTC(Npgsql timestamptz 仅接受 Kind=Utc)
        var utcRequest = request with
        {
            CompletedTime = request.CompletedTime.ToUniversalTime(),
            PreInjectionTime = request.PreInjectionTime.ToUniversalTime(),
            PostInjectionTime = request.PostInjectionTime.ToUniversalTime(),
        };

        var @event = mapper.Map<PassDataInjectionReceivedEvent>(utcRequest);
        await publishEndpoint.Publish(@event, cancellationToken);

        return Result.Success(true);
    }
}