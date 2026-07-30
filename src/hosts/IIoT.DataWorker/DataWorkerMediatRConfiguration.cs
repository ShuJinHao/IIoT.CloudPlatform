using IIoT.MasterDataService.Caching;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.Services.CrossCutting.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.DataWorker;

internal static class DataWorkerMediatRConfiguration
{
    internal static void Configure(MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssemblies(
            typeof(ReceiveHourlyCapacityCommand).Assembly,
            typeof(MfgProcessCreatedCacheInvalidationHandler).Assembly);
        configuration.AddOpenBehavior(
            typeof(DistributedLockBehavior<,>));
    }
}
