using AutoMapper;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;

namespace IIoT.ProductionService.Profiles;

public sealed class ProductionProfile : Profile
{
    public ProductionProfile()
    {
        CreateMap<ReceiveDeviceLogCommand, DeviceLogReceivedEvent>();
        CreateMap<ReceiveHourlyCapacityCommand, HourlyCapacityReceivedEvent>();
    }
}
