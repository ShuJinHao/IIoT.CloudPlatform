using AutoMapper;
using IIoT.ProductionService.Commands;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public record ReceiveStackingPassCommand(
    Guid DeviceId,
    StackingPassItemInput Item,
    string? RequestId = null
) : IDeviceCommand<Result<bool>>;

public record StackingPassItemInput(
    string Barcode,
    string TrayCode,
    int LayerCount,
    int SequenceNo,
    string CellResult,
    DateTime CompletedTime);

public sealed class ReceiveStackingPassHandler(
    IPassStationReceiveService receiveService,
    IMapper mapper
) : ICommandHandler<ReceiveStackingPassCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ReceiveStackingPassCommand request,
        CancellationToken cancellationToken)
    {
        var deduplicationKey = UploadDeduplicationKeys.ForStackingPass(request);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var @event = mapper.Map<PassDataStackingReceivedEvent>(request);
        return await receiveService.ValidateAndRegisterAsync(
            request.DeviceId,
            1,
            UploadMessageTypes.PassStationStacking,
            UploadDeduplicationKeys.NormalizeRequestId(request.RequestId),
            deduplicationKey.Value!,
            @event,
            cancellationToken);
    }
}
