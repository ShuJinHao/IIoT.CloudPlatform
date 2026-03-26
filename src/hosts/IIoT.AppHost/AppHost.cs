using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddResource(new CleanRedisResource("redis-cache"))
                   .WithImage("redis", "7.4-alpine")
                   .WithEndpoint(targetPort: 6379, name: "tcp");

var password = builder.AddParameter("pg-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: password)
    .WithImage("timescale/timescaledb", "latest-pg17")
    .WithDataVolume("postgres-iiot")
    .WithPgWeb()
    .AddDatabase("iiot-db");

var rabbitmq = builder.AddRabbitMQ("eventbus")
    .WithDataVolume("rabbitmq-iiot")
    .WithManagementPlugin();

var migration = builder.AddProject<Projects.IIoT_MigrationWorkApp>("iiot-migrationworkapp")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(redis);

var apiService = builder.AddProject<Projects.IIoT_HttpApi>("iiot-httpapi")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(migration);

builder.AddProject<Projects.IIoT_DataWorker>("iiot-dataworker")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WaitFor(migration);

builder.AddViteApp("iiot-web", "../../ui/iiot-web")
    .WithReference(apiService)
    .WithEnvironment("VITE_API_URL", apiService.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();

internal class CleanRedisResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{this.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{this.GetEndpoint("tcp").Property(EndpointProperty.Port)}");
}