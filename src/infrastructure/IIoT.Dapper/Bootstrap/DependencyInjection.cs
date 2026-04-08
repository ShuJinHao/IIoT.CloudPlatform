using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Dapper.Initializers;
using IIoT.Dapper.Production.QueryServices;
using IIoT.Dapper.Production.QueryServices.Capacity;
using IIoT.Dapper.Production.QueryServices.DeviceLog;
using IIoT.Dapper.Production.QueryServices.MfgProcess;
using IIoT.Dapper.Production.QueryServices.PassStation;
using IIoT.Dapper.Production.Repositories.Capacities;
using IIoT.Dapper.Production.Repositories.DeviceLogs;
using IIoT.Dapper.Production.Repositories.PassStations;
using IIoT.Dapper.TypeHandlers;
using IIoT.Services.Common.Contracts.DapperQueries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.Dapper.Bootstrap;

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

        // 记录表 schema 初始化器,由 MigrationWorkApp 在启动时显式调用
        builder.Services.AddScoped<RecordSchemaInitializer>();

        // 查询服务(跨聚合/记录类的只读查询入口,返回 DTO 不返回实体)
        builder.Services.AddScoped<IDeviceLogQueryService, DeviceLogQueryService>();
        builder.Services.AddScoped<ICapacityQueryService, CapacityQueryService>();
        builder.Services.AddScoped<IPassStationQueryService, PassStationQueryService>();
        builder.Services.AddScoped<IProcessUsageQueryService, ProcessUsageQueryService>();

        // 记录类写入仓储(由 Application 层用例调用)
        builder.Services.AddScoped<IDeviceLogRecordRepository, DeviceLogRecordRepository>();
        builder.Services.AddScoped<IHourlyCapacityRecordRepository, HourlyCapacityRecordRepository>();
        builder.Services.AddScoped<IPassDataInjectionRecordRepository, PassDataInjectionRecordRepository>();
    }
}