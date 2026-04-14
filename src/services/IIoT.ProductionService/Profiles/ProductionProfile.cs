using AutoMapper;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Common.Events.Capacities;
using IIoT.Services.Common.Events.DeviceLogs;
using IIoT.Services.Common.Events.PassStations;

namespace IIoT.ProductionService.Profiles;

public sealed class ProductionProfile : Profile
{
    public ProductionProfile()
    {
        CreateMap<ReceiveDeviceLogCommand, DeviceLogReceivedEvent>();
        CreateMap<ReceiveHourlyCapacityCommand, HourlyCapacityReceivedEvent>();
        CreateMap<ReceiveInjectionPassCommand, PassDataInjectionReceivedEvent>();
        CreateMap<InjectionPassItemInput, PassDataInjectionItem>()
            .ForMember(dest => dest.CompletedTime, opt => opt.MapFrom(src => src.CompletedTime.ToUniversalTime()))
            .ForMember(dest => dest.PreInjectionTime, opt => opt.MapFrom(src => src.PreInjectionTime.ToUniversalTime()))
            .ForMember(dest => dest.PostInjectionTime, opt => opt.MapFrom(src => src.PostInjectionTime.ToUniversalTime()));
    }
}
