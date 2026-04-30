using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.ProductionService;

public static class PassStationRegistration
{
    public static IServiceCollection AddPassStationRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IPassStationSchemaProvider, PassStationSchemaProvider>();
        services.AddScoped<IPassStationReceiveService, PassStationReceiveService>();
        services.AddTransient<
            IRequestHandler<GetPassStationTypesQuery, Result<IReadOnlyList<PassStationTypeDefinitionDto>>>,
            GetPassStationTypesHandler>();
        services.AddTransient<
            IRequestHandler<GetPassStationListByTypeQuery, Result<PagedList<PassStationListItemDto>>>,
            GetPassStationListByTypeHandler>();
        services.AddTransient<
            IRequestHandler<GetPassStationDetailByTypeQuery, Result<PassStationDetailDto>>,
            GetPassStationDetailByTypeHandler>();

        return services;
    }
}
