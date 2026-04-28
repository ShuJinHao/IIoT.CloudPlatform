using IIoT.Core.Production.Contracts.PassStation;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.ProductionService;

/// <summary>
/// 过站类型注册扩展。
/// 用来把不同过站事件的 mapper、持久化 handler 和查询 handler 统一接入 DI。
/// </summary>
public static class PassStationRegistration
{
    /// <summary>
    /// 注册某一种过站事件的写入链路。
    /// </summary>
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

    /// <summary>
    /// 注册某一种过站类型的查询链路。
    /// </summary>
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
