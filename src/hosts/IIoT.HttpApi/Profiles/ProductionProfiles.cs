using AutoMapper;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.Services.Common.Events;
using IIoT.Services.Common.Models;

namespace IIoT.HttpApi.Profiles;

public class ProductionProfiles : Profile
{
    public ProductionProfiles()
    {
        // Request → Command
        CreateMap<InjectionPassRequest, ReceiveInjectionPassCommand>();

        // Command → Event
        CreateMap<ReceiveInjectionPassCommand, PassDataInjectionReceivedEvent>();
        CreateMap<ReceiveDailyCapacityCommand, DailyCapacityReceivedEvent>();
    }
}