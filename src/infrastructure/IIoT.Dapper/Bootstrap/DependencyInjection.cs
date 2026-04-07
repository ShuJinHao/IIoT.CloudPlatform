using Dapper;
using IIoT.Dapper.Initializers;
using IIoT.Dapper.QueryServices.Capacity;
using IIoT.Dapper.QueryServices.DeviceLog;
using IIoT.Dapper.QueryServices.PassStation;
using IIoT.Dapper.Repositories.Capacities;
using IIoT.Dapper.Repositories.DeviceLogs;
using IIoT.Dapper.Repositories.PassStations;
using IIoT.Dapper.TypeHandlers;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.Services.Common.Contracts.RecordRepositories;
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
                ?? throw new InvalidOperationException("缺少 iiot-db 连接字符串");
            return new NpgsqlConnectionFactory(connStr);
        });

        builder.Services.AddScoped<RecordSchemaInitializer>();

        builder.Services.AddScoped<IPassStationQueryService, PassStationQueryService>();
        builder.Services.AddScoped<ICapacityQueryService, CapacityQueryService>();
        builder.Services.AddScoped<IDeviceLogQueryService, DeviceLogQueryService>();

        builder.Services.AddScoped<IDeviceLogRecordRepository, DeviceLogRecordRepository>();
        builder.Services.AddScoped<IHourlyCapacityRecordRepository, HourlyCapacityRecordRepository>();
        builder.Services.AddScoped<IPassDataInjectionRecordRepository, PassDataInjectionRecordRepository>();
    }
}