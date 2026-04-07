using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Dapper.Extensions;

public static class DapperServiceCollectionExtensions
{
    public static IServiceCollection AddRecordDapper(this IServiceCollection services)
    {
        services.AddScoped<RecordSchemaInitializer>();
        return services;
    }
}