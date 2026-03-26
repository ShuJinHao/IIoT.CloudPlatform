using IIoT.Dapper.QueryServices.Capacity;
using IIoT.Dapper.QueryServices.DeviceLog;
using IIoT.Dapper.QueryServices.PassStation;
using IIoT.Services.Common.Contracts.DapperQueries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.Dapper;

public static class DependencyInjection
{
    public static void AddDapper(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("iiot-db");

        // 设计时（EF Migration）可能没有连接字符串，跳过注册
        if (string.IsNullOrEmpty(connectionString)) return;

        builder.Services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        builder.Services.AddScoped<IPassStationQueryService, PassStationQueryService>();
        builder.Services.AddScoped<ICapacityQueryService, CapacityQueryService>();
        builder.Services.AddScoped<IDeviceLogQueryService, DeviceLogQueryService>();
    }
}