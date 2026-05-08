using Dapper;
using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Dapper.Initializers;
using IIoT.Dapper.Production.QueryServices.Capacity;
using IIoT.Dapper.Production.QueryServices.Device;
using IIoT.Dapper.Production.QueryServices.DeviceLog;
using IIoT.Dapper.Production.QueryServices.PassStation;
using IIoT.Dapper.Production.Repositories.Capacities;
using IIoT.Dapper.Production.Repositories.DeviceLogs;
using IIoT.Dapper.Production.Repositories.PassStations;
using IIoT.Dapper.TypeHandlers;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace IIoT.Dapper;

public static class DependencyInjection
{
    public static void AddDapper(this IHostApplicationBuilder builder)
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var postgresOptions = config.GetRequiredValidatedOptions<PostgresOptions>(
                PostgresOptions.SectionName,
                static options => options.Validate());

            var connStr = config.GetConnectionString(ConnectionResourceNames.IiotDatabase)
                ?? throw new InvalidOperationException($"Missing {ConnectionResourceNames.IiotDatabase} connection string.");
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connStr)
            {
                CommandTimeout = postgresOptions.CommandTimeoutSeconds
            };

            return new NpgsqlConnectionFactory(connectionStringBuilder.ConnectionString);
        });

        builder.Services.AddScoped<IRecordSchemaInitializer, RecordSchemaInitializer>();

        builder.Services.AddScoped<IDeviceLogQueryService, DeviceLogQueryService>();
        builder.Services.AddScoped<ICapacityQueryService, CapacityQueryService>();
        builder.Services.AddScoped<IDeviceIdentityQueryService, DeviceIdentityQueryService>();
        builder.Services.AddScoped<IDeviceOperationalStatusQueryService, DeviceOperationalStatusQueryService>();
        builder.Services.AddScoped<IDeviceDeletionDependencyQueryService, DeviceDeletionDependencyQueryService>();
        builder.Services.AddScoped<IPassStationRecordQueryService, PassStationRecordQueryService>();

        builder.Services.AddScoped<IDeviceLogRecordRepository, DeviceLogRecordRepository>();
        builder.Services.AddScoped<IHourlyCapacityRecordRepository, HourlyCapacityRecordRepository>();
        builder.Services.AddScoped<IPassStationRecordRepository, PassStationRecordRepository>();
    }
}
