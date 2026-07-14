# CLOUD-ARCH-001 编译型架构契约

本契约是 Cloud 分层、DDD/仓储、数据库 owner、AI Read 只读、授权元数据和生产依赖边界的当前正式规则。它由 `IIoT.CloudPlatform.Analyzers` 在每个 Cloud 生产项目编译时执行，所有诊断默认为 `Error`。

Analyzer 项目固定为 `netstandard2.0`，`Microsoft.CodeAnalysis.CSharp` 固定 `5.6.0`。`Directory.Build.props` 仅将它作为 Analyzer 附加到 `IIoT.*` 生产项目；测试项目和 Analyzer 自身不引用其运行时程序集。

## CLOUDARCH001 分层依赖

- `IIoT.SharedKernel*` 不依赖 Core、Service、Infrastructure 或 Host。
- `IIoT.Core.*` 不依赖 Service、Infrastructure 或 Host。
- Service 不依赖 Infrastructure 或 Host。
- Infrastructure 不依赖 Host。
- Host 是组合根，可以组合内层与 Infrastructure。

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

Analyzer 使用 Roslyn `IOperation` 和同编译单元方法调用图追踪。AiRead 授权审计属于独立 pipeline behavior，不得作为 handler/helper 写入例外。

## CLOUDARCH005 请求分类与授权元数据

- 每个 HTTP request 只能实现 Human、Device、AnonymousBootstrap、Public、AiRead 中一个 request-kind marker。
- `IAiReadRequest<>` 必须携带 `AuthorizeAiRead("AiRead.<permission>")`，`AiRead.` 后必须有非空实际权限后缀；即使当前项目未引用授权特性所在程序集，Analyzer 仍必须报告缺少授权，禁止因 attribute symbol 不可见而 fail-open。非 AiRead 请求不得携带该特性。
- `AuthorizeRequirement` 和 `AdminOnly` 只能用于 `IHumanRequest<>`，不得混入 Device、Bootstrap、Public 或 AiRead。
- 继承得到的特性与直接特性使用同一规则。

## CLOUDARCH006 生产依赖测试资产

Cloud 生产程序集不得引用 `*.Tests`、`*.Testing`、`*TestKit*`、xUnit/NUnit/Moq 或 TestPlatform 程序集。`InternalsVisibleTo` 不是程序集依赖，不属于该禁止项。

## AnalyzerTests 与编译 fixture

`IIoT.CloudPlatform.AnalyzerTests` 是独立 required 测试项目，覆盖稳定 ID、默认 Error、正例、反例、alias、generic、helper、跨文件、inheritance 和 no-false-positive。`scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh` 另外创建临时真实 csproj，要求合法 fixture build 成功，每个非法 fixture 必须只以预期 `CLOUDARCH*` 稳定失败。这两层都是 `cloud-ci / build-test` required 门禁，不可 `continue-on-error`。

验证命令：

```bash
dotnet build IIoT.CloudPlatform.slnx -c Release --disable-build-servers --nologo -noAutoResponse
dotnet test src/tests/IIoT.CloudPlatform.AnalyzerTests/IIoT.CloudPlatform.AnalyzerTests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo -noAutoResponse
bash scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh
```
