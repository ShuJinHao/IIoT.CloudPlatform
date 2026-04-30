using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public interface IPassStationReceiveService
{
    Task<Result<bool>> ValidateAndRegisterAsync(
        Guid deviceId,
        int itemCount,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IPassStationEvent @event,
        CancellationToken cancellationToken);
}
