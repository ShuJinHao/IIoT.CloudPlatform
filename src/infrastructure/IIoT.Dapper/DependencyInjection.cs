using Dapper;
using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Dapper.Initializers;
using IIoT.Dapper.Production.PassStations;
using IIoT.Dapper.Production.QueryServices.Capacity;
using IIoT.Dapper.Production.QueryServices.Device;
using IIoT.Dapper.Production.QueryServices.DeviceLog;
using IIoT.Dapper.Production.QueryServices.PassStation;
using IIoT.Dapper.Production.Repositories.Capacities;
using IIoT.Dapper.Production.Repositories.DeviceLogs;
using IIoT.Dapper.Production.Repositories.PassStations;
using IIoT.Dapper.TypeHandlers;
using IIoT.Services.Common.Contracts.RecordQueries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.Dapper;

public static class DependencyInjection
{
    public static void AddDapper(this IHostApplicationBuilder builder)
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString("iiot-db")
                ?? throw new InvalidOperationException("\u7F3A\u5C11 iiot-db \u8FDE\u63A5\u5B57\u7B26\u4E32");
            return new NpgsqlConnectionFactory(connStr);
        });

        builder.Services.AddScoped<IRecordSchemaInitializer, RecordSchemaInitializer>();

        builder.Services.AddScoped<IDeviceLogQueryService, DeviceLogQueryService>();
        builder.Services.AddScoped<ICapacityQueryService, CapacityQueryService>();
        builder.Services.AddScoped<IDeviceIdentityQueryService, DeviceIdentityQueryService>();
        builder.Services.AddScoped<IDeviceDeletionDependencyQueryService, DeviceDeletionDependencyQueryService>();
        builder.Services.AddScoped(typeof(IPassStationQueryService<>), typeof(PassStationQueryService<>));

        builder.Services.AddScoped<IDeviceLogRecordRepository, DeviceLogRecordRepository>();
        builder.Services.AddScoped<IHourlyCapacityRecordRepository, HourlyCapacityRecordRepository>();
        builder.Services.AddScoped(typeof(IPassStationRepository<>), typeof(PassStationRepository<>));

        builder.Services.AddSingleton<IPassStationWriteSql<InjectionWriteModel>, InjectionPassStationSql>();
        builder.Services.AddSingleton<IPassStationQuerySql<InjectionPassListItemDto>, InjectionPassStationSql>();
        builder.Services.AddSingleton<IPassStationQuerySql<InjectionPassDetailDto>, InjectionPassStationSql>();
    }
}
