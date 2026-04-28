using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using IIoT.MigrationWorkApp;
using IIoT.SharedKernel.Configuration;
using Npgsql;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [Fact]
    public async Task HumanIdentity_Refresh_ShouldBeRejectedAfterEmployeeDeactivation()
    {
        _fixture.ClearAuthToken();

        using var loginResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ReadIssuedAuthSessionAsync(loginResponse);
        _fixture.SetAuthToken(session.AccessToken);

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var adminEmployeeId = await GetEmployeeIdByEmployeeNoAsync(connectionString, IIoTAppFixture.SeedAdminEmployeeNo);
        using (var deactivateRequest = new HttpRequestMessage(
                   HttpMethod.Put,
                   $"/api/v1/human/employees/{adminEmployeeId}/deactivate"))
        using (var deactivateResponse = await _fixture.HttpClient.SendAsync(deactivateRequest))
        {
            deactivateResponse.IsSuccessStatusCode.Should().BeTrue();
        }

        _fixture.ClearAuthToken();
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/human/identity/refresh");
        refreshRequest.Headers.Add("X-IIoT-Refresh-Token", session.RefreshToken);

        using var refreshResponse = await _fixture.HttpClient.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EdgeBootstrap_Refresh_ShouldBeRejectedAfterDeviceDeletion()
    {
        await AuthenticateAsAdminAsync();
        var device = await CreateTestDeviceRegistrationAsync("refresh-delete");
        _fixture.ClearAuthToken();

        using var bootstrapResponse = await _fixture.HttpClient.GetAsync(
            $"/api/v1/bootstrap/device-instance?clientCode={Uri.EscapeDataString(device.Code)}");
        bootstrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ReadIssuedBootstrapSessionAsync(bootstrapResponse);

        await AuthenticateAsAdminAsync();
        using (var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/human/devices/{device.DeviceId}"))
        using (var deleteResponse = await _fixture.HttpClient.SendAsync(deleteRequest))
        {
            deleteResponse.IsSuccessStatusCode.Should().BeTrue();
        }

        _fixture.ClearAuthToken();
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bootstrap/edge-refresh");
        refreshRequest.Headers.Add("X-IIoT-Refresh-Token", session.RefreshToken);

        using var refreshResponse = await _fixture.HttpClient.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HourlyCapacity_CompatibilitySql_ShouldPromoteLegacyPrimaryKeyToCompositeKey()
    {
        var sourceConnectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var databaseName = $"compat_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        var targetConnectionString = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        await CreateDatabaseAsync(adminConnectionString, databaseName);
        try
        {
            await ExecuteNonQueryAsync(
                targetConnectionString,
                """
                create table hourly_capacity
                (
                    id uuid not null,
                    date date not null,
                    total_count integer not null default 0,
                    constraint hourly_capacity_pkey primary key (id)
                );
                """);

            var compatibilitySql = GetHourlyCapacityCompatibilitySql();
            await ExecuteNonQueryAsync(targetConnectionString, compatibilitySql);

            var primaryKeyColumns = await GetPrimaryKeyColumnsAsync(targetConnectionString, "hourly_capacity");

            primaryKeyColumns.Should().Equal("id", "date");
        }
        finally
        {
            await DropDatabaseAsync(adminConnectionString, databaseName);
        }
    }

    private static string GetHourlyCapacityCompatibilitySql()
    {
        var field = typeof(DatabaseInitializationOrchestrator).GetField(
            "NormalizeHourlyCapacityPrimaryKeySql",
            BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Unable to locate hourly_capacity compatibility SQL.");

        return (string)(field.GetRawConstantValue()
                         ?? throw new InvalidOperationException("Compatibility SQL field was empty."));
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand($"""create database "{databaseName}" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using var terminate = new NpgsqlCommand(
            """
            select pg_terminate_backend(pid)
            from pg_stat_activity
            where datname = @databaseName and pid <> pg_backend_pid();
            """,
            connection);
        terminate.Parameters.AddWithValue("databaseName", databaseName);
        await terminate.ExecuteNonQueryAsync();

        await using var drop = new NpgsqlCommand($"""drop database if exists "{databaseName}" """, connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> GetPrimaryKeyColumnsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select attribute.attname
            from pg_constraint con
            join pg_class relation on relation.oid = con.conrelid
            join unnest(con.conkey) with ordinality as columns(attnum, ordinality) on true
            join pg_attribute attribute on attribute.attrelid = relation.oid
                                       and attribute.attnum = columns.attnum
            where relation.relname = @tableName
              and con.contype = 'p'
            order by columns.ordinality;
            """,
            connection);
        command.Parameters.AddWithValue("tableName", tableName);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns.ToArray();
    }

    private static async Task<Guid> GetEmployeeIdByEmployeeNoAsync(string connectionString, string employeeNo)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select id
            from employees
            where employee_no = @employeeNo
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue("employeeNo", employeeNo);

        var result = await command.ExecuteScalarAsync();
        return result is Guid id
            ? id
            : throw new InvalidOperationException($"Unable to locate employee '{employeeNo}'.");
    }

}
