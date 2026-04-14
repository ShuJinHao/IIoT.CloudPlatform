using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.PassStations;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public record PersistPassStationCommand<TEvent>(TEvent Event)
    : ICommand<Result<bool>>
    where TEvent : class, IPassStationEvent;

public interface IPassStationPersister<in TEvent>
    where TEvent : class, IPassStationEvent
{
    Task<Result<bool>> PersistAsync(TEvent evt, CancellationToken cancellationToken);
}

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
