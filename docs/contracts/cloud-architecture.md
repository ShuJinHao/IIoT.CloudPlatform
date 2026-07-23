# CLOUD-ARCH-001 编译型架构契约

本契约是 Cloud 分层、DDD/仓储、数据库 owner、AI Read 只读、授权元数据和生产依赖边界的当前正式规则。它由 `IIoT.CloudPlatform.Analyzers` 在每个 Cloud 生产项目编译时执行，所有诊断默认为 `Error`。

Analyzer 项目固定为 `netstandard2.0`，`Microsoft.CodeAnalysis.CSharp` 固定 `5.6.0`。`Directory.Build.props` 仅将它作为 Analyzer 附加到 `IIoT.*` 生产项目；测试项目和 Analyzer 自身不引用其运行时程序集。

## CLOUDARCH001 分层依赖

- `IIoT.SharedKernel*` 不依赖 Core、Service、Infrastructure 或 Host。
- `IIoT.Core.*` 不依赖 Service、Infrastructure 或 Host。
- Service 不依赖 Infrastructure 或 Host。
- Infrastructure 不依赖 Host。
- Host 是组合根，可以组合内层与 Infrastructure。
- 任何新的 `IIoT.*` 生产 assembly 必须能被明确归入上述层；未分类项目必须以 `CLOUDARCH001` fail-closed，不得因 Analyzer 未识别新名称而逃逸。

诊断依据 Roslyn `ReferencedAssemblyNames` 和稳定程序集分层，不读取 csproj 文本做字符串猜测。

## CLOUDARCH002 聚合与通用仓储

- 真实聚合根只能在 `IIoT.Core.*` 声明，并显式实现 `IAggregateRoot`。
- `IRepository<T>` / `IReadRepository<T>` 的 `T` 必须实现 `IAggregateRoot`。
- 投影、状态、审计、Outbox、refresh token 和幂等登记不得通过别名、泛型 wrapper 或继承伪装成聚合根。

该规则使用符号、`IOperation` 结果类型和泛型约束判定，别名不是例外。字段、参数、属性、返回值以及 local `var` 中的泛型 resolver/object creation 都必须检查；`GetRequiredService<IRepository<Projection>>()` 不能绕过聚合约束。

## CLOUDARCH003 数据库 owner

Core 和 Service 不得直接持有或调用 EF Core、Dapper、Npgsql、ADO.NET 类型/API；应在内层定义端口，并由 Infrastructure 实现。Host 默认同样受限，只保留下列可证明的 owner 边界：

- 项目级：`IIoT.MigrationWorkApp`。它是专用 migration/schema compatibility/seed host，整个可执行程序的单一职责就是数据库初始化。
- 类型级：`IIoT.DataWorker::Program`，仅容纳当前数据库 readiness probe。
- 类型级：`IIoT.HttpApi::IIoT.HttpApi.DesignTimeDbContextFactory`，仅用于 EF design-time factory。
- 类型级：`IIoT.HttpApi::IIoT.HttpApi.Infrastructure.PostgresReadinessHealthCheck`，仅用于 PostgreSQL readiness probe。

类型例外必须用 `assembly::fully-qualified-type` 成对声明。同程序集的相邻 Controller/Worker 仍必须失败；禁止把 DataWorker 或 HttpApi 整仓放行。例外只存在于根 `.globalconfig`，新增例外必须同批增加精确正/反 fixture 并更新本契约。

## CLOUDARCH004 AI Read 只读调用图

`IAiReadRequest<>` handler 不得到达任何业务写路径，包括：

- `IRepository` 的 Add/Update/Delete/Remove/SaveChanges。
- EF Core、Dapper、Npgsql 或 ADO.NET 的写入/SaveChanges/Execute API。
- 通过 MediatR 发送 `ICommand<>`。
- 隐藏在别名、泛型 helper、继承 handler 或跨文件 helper 中的上述写入。

Analyzer 使用 Roslyn `IOperation` 和方法调用图追踪，必须识别字段、属性、局部变量和参数中存储后再调用的委托、隐式编译器调用、类型初始化器、默认接口实现、外部方法以及由 `IAiReadRequest<>` 约束确定的泛型 handler。AiRead 授权审计属于独立 pipeline behavior，不得作为 handler/helper 写入例外。

跨项目调用图必须 fail-closed：

- `IReadOnlyQueryPort` 与 `IAiReadScopeAccessor` 只在整个公开能力面递归不暴露可写 capability 时才可作为只读根；返回值、参数、属性、事件、字段、数组、指针、函数指针、delegate、Task/泛型/集合中的仓储、Dapper、Npgsql、EF、ADO.NET 或自定义可写 wrapper 都必须使标记失效。`ISpecification<T>` 是查询值描述，不是跨项目执行端口，不得取得只读标记信任。
- 每个受管 ProjectReference 都必须由编译 target 生成稳定仓库相对 csproj 身份，并由 source generator 在程序集写入 schema、条目数、SHA-256、来源身份和每个 method/field effect。消费方只接受当前 schema、唯一 manifest、来源身份匹配且 digest 精确的摘要；旧 schema、缺失/重复/损坏 manifest、手写伪造摘要、非 ProjectReference 预编译引用或未登记引用全部 fail-closed。
- Analyzer 必须继续追踪受管外部程序集的安全/写 effect；public virtual/interface 等开放分派、未解析生命周期边界和无可信摘要的 `IIoT.*` 调用必须报 `CLOUDARCH004`。摘要不能把开放分派、static initializer、field initializer、callback、command、raw SQL 或写端口伪装成安全调用。
- 标记端口的真实实现方法仍是新的根；该实现到达 Dapper `Execute*`、EF 写 API、仓储写入或命令发送时必须失败。受管 ProjectReference 中的 public concrete 与 public infrastructure port 也必须由 effect summary 继续分析，不能靠 concrete coupling、命名 allowlist 或兼容 wrapper 绕过调用图。有真实外部调用方的生产 API 不得只为简化 Analyzer 而收窄；但返回 raw database capability 的 public factory 在读端口调用图中属于不可审计的开放分派，经三仓零外部调用证据确认后必须收口为本程序集内部实现，不得通过新增重复 read factory/port 或放宽 open-dispatch 门禁换绿。

Raw SQL 只读判定必须 fail-closed：

- EF Core `ExecuteSqlRaw*` / `ExecuteSqlInterpolated*` 始终是写 sink，即使 SQL 文本看似 `SELECT` 也不放行。
- Dapper `Query*` 和 ADO.NET `ExecuteReader*` / `ExecuteScalar*` 只有在 SQL 是编译期常量、词法完整、单语句且注释与字符串剥离后严格为只读 `SELECT` / CTE 时才允许。
- 动态 SQL、未闭合注释/字符串、多语句、DML/DDL、写 CTE 或无法证明 SQL 文本的 raw database 调用必须报 `CLOUDARCH004`。
- 唯一有界例外是直接标记端口的直接实现方法使用 Dapper `Query*` + `CommandDefinition`：此时只信任 Dapper 的读 API 分类，实现方法本身仍作为 Analyzer 根扫描，`Execute*` 仍是写 sink。普通 AiRead handler/helper 中的动态 `CommandDefinition` 不享受该例外。

## CLOUDARCH005 请求分类与授权元数据

- 每个 HTTP request 只能实现 Human、Device、AnonymousBootstrap、Public、AiRead 中一个 request-kind marker。
- `IAiReadRequest<>` 必须携带 `AuthorizeAiRead("AiRead.<permission>")`，`AiRead.` 后必须有非空实际权限后缀；即使当前项目未引用授权特性所在程序集，Analyzer 仍必须报告缺少授权，禁止因 attribute symbol 不可见而 fail-open。非 AiRead 请求不得携带该特性。
- `AuthorizeRequirement` 和 `AdminOnly` 只能用于 `IHumanRequest<>`，不得混入 Device、Bootstrap、Public 或 AiRead。
- 继承得到的特性与直接特性使用同一规则。

## CLOUDARCH006 生产依赖测试资产

Cloud 生产程序集不得引用 `*.Tests`、`*.Testing`、`*TestKit*`、xUnit/NUnit/Moq 或 TestPlatform 程序集。`InternalsVisibleTo` 不是程序集依赖，不属于该禁止项。

## CLOUDARCH007 安全读取禁止缓存

`IPermissionProvider`、`IDevicePermissionService` 和 `IDeviceIdentityQueryService` 的生产实现必须每次读取权威存储，不得直接或间接到达 `ICacheService`。别名、泛型 helper、接口分派、跨文件 wrapper 和同编译单元调用链都不是例外；权限展示目录等非授权 DTO 可按 `CLOUD-CACHE-001` 另行分类。

## CLOUDARCH008 JWT 必须经过验签

Cloud 生产程序集禁止调用 `System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.ReadJwtToken`。认证、授权和身份链只能从 ASP.NET Core JwtBearer 完成 signing key、issuer、audience、lifetime 验证后的 `ClaimsPrincipal` 读取。Analyzer 以精确方法符号判定，不因同名用户方法误报；测试程序集可解析由真实服务器签发的测试 token。

## CLOUDARCH009 Services.Common 已退役

生产类型不得声明在 `IIoT.Services.Common` 或其子命名空间。契约归入 `IIoT.Services.Contracts`，横切行为归入 `IIoT.Services.CrossCutting`；不保留 alias、adapter、wrapper、fallback 或影子命名空间。`IIoT.Services.Commonplace` 等非精确命名空间不得误报。

## CLOUDARCH010 连接资源常量

`iiot-db` 和 `eventbus` 生产字面量只允许由 `IIoT.SharedKernel.Configuration.ConnectionResourceNames` 的精确类型自身声明。AppHost、host、service 和 infrastructure 必须引用该权威常量，不得用重复 const、wrapper 或 fallback 重建第二条资源名路径。

## AnalyzerTests 与编译 fixture

`IIoT.CloudPlatform.AnalyzerTests` 是独立 Architecture 测试项目，按当前 discovery 覆盖稳定 ID、默认 Error、正反例、generic/委托/隐式调用/生命周期、递归 writable capability、跨程序集 effect summary、开放分派、static field initializer、strict raw SQL、未分类生产层与 no-false-positive。Analyzer 必须对生成代码继续执行分析并上报，不能用 `<auto-generated/>` 或 `.g.cs` 逃逸。`scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh` 创建临时真实 csproj，覆盖 valid/invalid、call-graph bypass、suppression bypass 与 Contracts metadata binding；fixture 集合随已启用规则同批演进，不以历史数量作为门禁。安全缓存正反例必须以 `ProjectReference` 引用真实 `IIoT.Services.Contracts.csproj`，binding probe 必须从真实 build output 证明 Analyzer 使用的当前 metadata identity 被唯一解析；任一缺失、改名或新增未登记 identity 都必须 fail-closed。旧 schema、来源不匹配、重复伪造 manifest、非受管预编译引用和损坏 digest 都只能以 `CLOUDARCH004` 失败。

验证命令：

```bash
dotnet build IIoT.CloudPlatform.slnx -c Release --disable-build-servers --nologo -noAutoResponse
dotnet test src/tests/IIoT.CloudPlatform.AnalyzerTests/IIoT.CloudPlatform.AnalyzerTests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse
bash scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh
```
