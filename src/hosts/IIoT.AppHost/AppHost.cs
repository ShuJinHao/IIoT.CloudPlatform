var builder = DistributedApplication.CreateBuilder(args);

// 1. 定义基础设施资源 (名字必须与底层注入一致)
var redis = builder.AddRedis("redis-cache");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("iiot-db");

// 2. 配置迁移助手 (MigrationWorkApp)
// 职责：它是 API 的先遣部队，干完活会自动退出
var migration = builder.AddProject<Projects.IIoT_MigrationWorkApp>("iiot-migrationworkapp")
    .WithReference(postgres)
    .WithReference(redis);

// 3. 配置主 API 中台 (HttpApi)
// 🌟 核心优化：使用 .WaitFor(migration) 确保数据库和权限种子就绪后再启动
builder.AddProject<Projects.IIoT_HttpApi>("iiot-httpapi")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(migration);

builder.Build().Run();