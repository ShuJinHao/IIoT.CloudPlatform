using AutoMapper;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public record ReceiveStackingPassCommand(
    Guid DeviceId,
    StackingPassItemInput Item
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
        var @event = mapper.Map<PassDataStackingReceivedEvent>(request);
        return await receiveService.ValidateAndPublishAsync(
            request.DeviceId,
            1,
            @event,
            cancellationToken);
    }
}
