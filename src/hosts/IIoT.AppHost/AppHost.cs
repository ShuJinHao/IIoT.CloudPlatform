using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddResource(new CleanRedisResource("redis-cache"))
                   .WithImage("redis", "7.4-alpine")
                   .WithEndpoint(targetPort: 6379, name: "tcp");

var password = builder.AddParameter("pg-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: password)
    .WithDataVolume("postgres-iiot")
    .WithPgWeb()
    .AddDatabase("iiot-db");

var migration = builder.AddProject<Projects.IIoT_MigrationWorkApp>("iiot-migrationworkapp")
    .WithReference(postgres)
    .WaitFor(postgres)
    // 🌟 2. 直接引用，它会自动生成完美的 host:port 字符串！
    .WithReference(redis);

// 1. 将 HttpApi 的引用提取为一个变量 apiService (如果还没提的话)
var apiService = builder.AddProject<Projects.IIoT_HttpApi>("iiot-httpapi")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(migration);

builder.AddViteApp("iiot-web", "../../ui/iiot-web")
    .WithReference(apiService)
    .WithEnvironment("VITE_API_URL", apiService.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.IIoT_DataWorker>("iiot-dataworker");

builder.Build().Run();

// =========================================================================
// 🌟 魔法黑科技：自定义一个纯净版 Redis 资源类
// =========================================================================
internal class CleanRedisResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    // 核心精髓：精准剥离出动态分配的 IP 和端口，强行组装成 "host:port" 格式，彻底踢掉导致报错的 "tcp://" 前缀！
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{this.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{this.GetEndpoint("tcp").Property(EndpointProperty.Port)}");
}