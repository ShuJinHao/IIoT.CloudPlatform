using IIoT.Services.Common.Events.PassStations;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public interface IPassStationReceiveService
{
    Task<Result<bool>> ValidateAndPublishAsync<TEvent>(
        Guid deviceId,
        int itemCount,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IPassStationEvent;
}
