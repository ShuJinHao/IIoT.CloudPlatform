using IIoT.Core.Production.Contracts.PassStation;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Common.Events.PassStations;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.ProductionService;

public static class PassStationRegistration
{
    public static IServiceCollection AddPassStationType<TEvent, TWriteModel, TMapper>(
        this IServiceCollection services)
        where TEvent : class, IPassStationEvent
        where TWriteModel : IPassStationWriteModel
        where TMapper : class, IPassStationMapper<TEvent, TWriteModel>
    {
        services.AddScoped<IPassStationMapper<TEvent, TWriteModel>, TMapper>();
        services.AddScoped<IPassStationPersister<TEvent>, PassStationPersister<TEvent, TWriteModel>>();
        services.AddTransient<
            IRequestHandler<PersistPassStationCommand<TEvent>, Result<bool>>,
            PersistPassStationHandler<TEvent>>();

        return services;
    }

    public static IServiceCollection AddPassStationQuery<TListDto, TDetailDto>(
        this IServiceCollection services)
        where TListDto : class
        where TDetailDto : class
    {
        services.AddTransient<
            IRequestHandler<GetPassStationListQuery<TListDto>, Result<PagedList<TListDto>>>,
            GetPassStationListHandler<TListDto>>();
        services.AddTransient<
            IRequestHandler<GetPassStationLatest200Query<TListDto>, Result<PagedList<TListDto>>>,
            GetPassStationLatest200Handler<TListDto>>();
        services.AddTransient<
            IRequestHandler<GetPassStationDetailQuery<TDetailDto>, Result<TDetailDto>>,
            GetPassStationDetailHandler<TDetailDto>>();

        return services;
    }
}
