using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using IIoT.SharedKernel.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddResource(new CleanRedisResource("redis-cache"))
                   .WithImage("redis", "7.4-alpine")
                   .WithEndpoint(targetPort: 6379, name: "tcp");

var password = builder.AddParameter("pg-password", secret: true);
var seedAdminNo = builder.AddParameter("seed-admin-no");
var seedAdminPassword = builder.AddParameter("seed-admin-password", secret: true);
var postgresVolumeName = builder.Configuration["AppHost:PostgresVolumeName"] ?? "postgres-iiot";
var rabbitMqVolumeName = builder.Configuration["AppHost:RabbitMqVolumeName"] ?? "rabbitmq-iiot";

var postgres = builder.AddPostgres("postgres", password: password)
    .WithImage("timescale/timescaledb", "latest-pg17")
    .WithDataVolume(postgresVolumeName)
    .WithArgs("-c", "shared_preload_libraries=timescaledb")
    .WithPgWeb()
    .AddDatabase(ConnectionResourceNames.IiotDatabase);

var rabbitmq = builder.AddRabbitMQ(ConnectionResourceNames.EventBus)
    .WithDataVolume(rabbitMqVolumeName)
    .WithManagementPlugin();

var migration = builder.AddProject<Projects.IIoT_MigrationWorkApp>("iiot-migrationworkapp")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(redis)
    .WithEnvironment("SEED_ADMIN_NO", seedAdminNo)
    .WithEnvironment("SEED_ADMIN_PASSWORD", seedAdminPassword);

var apiService = builder.AddProject<Projects.IIoT_HttpApi>("iiot-httpapi")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitForCompletion(migration);

var gatewayService = builder.AddProject<Projects.IIoT_Gateway>("iiot-gateway")
    .WithReference(apiService)
    .WithEnvironment(
        "ReverseProxy__Clusters__httpapi__Destinations__primary__Address",
        apiService.GetEndpoint("http"))
    .WaitFor(apiService);

builder.AddProject<Projects.IIoT_DataWorker>("iiot-dataworker")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitForCompletion(migration);

builder.AddViteApp("iiot-web", "../../ui/iiot-web")
    .WithReference(gatewayService)
    .WithEnvironment("VITE_API_URL", gatewayService.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();

internal class CleanRedisResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{this.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{this.GetEndpoint("tcp").Property(EndpointProperty.Port)}");
}
