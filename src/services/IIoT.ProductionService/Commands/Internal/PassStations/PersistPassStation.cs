using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.PassStations;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

/// <summary>
/// 泛型过站落库命令。
/// 把某一种过站事件转交给对应的 persister 处理。
/// </summary>
public record PersistPassStationCommand<TEvent>(TEvent Event)
    : ICommand<Result<bool>>
    where TEvent : class, IPassStationEvent;

/// <summary>
/// 过站持久化抽象。
/// 不同过站类型共用统一接口，内部再由具体 mapper 和 repository 完成落库。
/// </summary>
public interface IPassStationPersister<in TEvent>
    where TEvent : class, IPassStationEvent
{
    Task<Result<bool>> PersistAsync(TEvent evt, CancellationToken cancellationToken);
}

/// <summary>
/// 泛型过站持久化实现。
/// 先校验设备是否存在，再把事件映射成写模型并批量写入记录库。
/// </summary>
public sealed class PassStationPersister<TEvent, TWriteModel>(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IPassStationRepository<TWriteModel> repository,
    IPassStationMapper<TEvent, TWriteModel> mapper)
    : IPassStationPersister<TEvent>
    where TEvent : class, IPassStationEvent
    where TWriteModel : IPassStationWriteModel
{
    public async Task<Result<bool>> PersistAsync(TEvent evt, CancellationToken cancellationToken)
    {
        var exists = await deviceIdentityQuery.ExistsAsync(evt.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure($"过站数据落库失败:设备 {evt.DeviceId} 不存在");

        var writeModels = mapper.ToWriteModels(evt, DateTime.UtcNow);
        if (writeModels.Count == 0)
            return Result.Success(true);

        await repository.InsertBatchAsync(writeModels, cancellationToken);
        return Result.Success(true);
    }
}

/// <summary>
/// 过站落库命令处理器。
/// 只负责把命令转发给对应的 persister。
/// </summary>
public sealed class PersistPassStationHandler<TEvent>(
    IPassStationPersister<TEvent> persister)
    : ICommandHandler<PersistPassStationCommand<TEvent>, Result<bool>>
    where TEvent : class, IPassStationEvent
{
    public Task<Result<bool>> Handle(
        PersistPassStationCommand<TEvent> request,
        CancellationToken cancellationToken)
        => persister.PersistAsync(request.Event, cancellationToken);
}
