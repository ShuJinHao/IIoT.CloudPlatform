using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.Uploads;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public interface IPassStationReceiveService
{
    Task<Result<EdgeUploadAcceptedResponse>> ValidateAndRegisterAsync(
        Guid deviceId,
        int itemCount,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IPassStationEvent @event,
        CancellationToken cancellationToken);
}
