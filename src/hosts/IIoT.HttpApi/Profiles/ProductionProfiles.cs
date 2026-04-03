using AutoMapper;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Common.Events;

namespace IIoT.HttpApi.Profiles;

public class ProductionProfiles : Profile
{
    public ProductionProfiles()
    {
        CreateMap<ReceiveInjectionPassCommand, PassDataInjectionReceivedEvent>();
        CreateMap<ReceiveHourlyCapacityCommand, HourlyCapacityReceivedEvent>();
    }
}