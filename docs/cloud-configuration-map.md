# 云端配置地图

本文档只覆盖 `IIoT.CloudPlatform`。当前云端入口拓扑已经调整为：

- `Nginx` 最外层
- `IIoT.Gateway`（YARP）中间层
- `IIoT.HttpApi` 单体后端

当前阶段统一采用：

- 单 IP
- HTTP
- 路径分流
- 不购买域名
- 不启用正式 TLS/证书

## 配置来源优先级

### 部署脚本

`build-push.ps1` 与 `deploy.ps1` 统一使用以下优先级：

`脚本参数 > .env > 进程环境变量`

也就是说：

1. 显式传入脚本的参数优先级最高
2. 未传参数时读取 `src/hosts/IIoT.AppHost/aspirate-output/.env`
3. `.env` 未配置时，才回退到当前 PowerShell 进程的环境变量

### 云端宿主 / ASP.NET Core

云端宿主继续保留标准 .NET 配置分层：

1. `appsettings.json`
2. 环境变量
3. Aspire 参数 / 用户机密 / 部署模板注入

`appsettings.json` 只允许保留安全默认值与 section 结构，不允许提交真实密码、真实连接串、真实公网地址。

## 宿主运行时配置

### AppHost

路径：

- `src/hosts/IIoT.AppHost/AppHost.cs`
- `src/hosts/IIoT.AppHost/appsettings.json`

职责：

- 声明 Aspire 参数与资源编排关系
- 编排 `iiot-httpapi`、`iiot-gateway`、`iiot-dataworker`、`iiot-migrationworkapp`
- 为前端开发时的 `VITE_API_URL` 指向 `iiot-gateway`
- 可选为 Cloud Web 注入 `VITE_AICOPILOT_CHALLENGE_URL`，仅用于跳转 AICopilot OIDC challenge，不承载 Cloud token

规则：

- 不提交真实数据库密码
- 不提交真实注册表地址
- `aspirate-state.json` 视为生成态文件，不入仓

### Gateway

路径：

- `src/hosts/IIoT.Gateway/Program.cs`
- `src/hosts/IIoT.Gateway/appsettings.json`

职责：

- 作为 YARP 入口层宿主
- 只负责路径转发、路径重写、入口语义标签
- 不承载业务逻辑、CQRS handler、权限替代、设备绑定替代

当前固定的外部路由面：

- `/api/v1/human/*`
- `/api/v1/edge/*`
- `/api/v1/bootstrap/*`
- `/api/v1/ai/read/*`
- `/api/v1/ai/identity/*`

其中 `/api/v1/ai/identity/*` 只用于 AICopilot 读取 Cloud 身份状态版本，必须走 AI service account JWT + `AiRead.IdentityStatus`，不提供 Cloud 业务写能力。

当前 bootstrap 对外入口只允许走 Gateway 公共路径：

- `/api/v1/bootstrap/device-instance`
- `/api/v1/bootstrap/edge-refresh`
- `/api/v1/bootstrap/edge-login`

`/api/v1/edge/bootstrap/device-instance` 和 `/api/v1/human/identity/edge-login` 不再作为外部支持路径。

### HttpApi

路径：

- `src/hosts/IIoT.HttpApi/appsettings.json`

保留 section：

- `DistributedLock`
- `RateLimiting`
- `ForwardedHeaders`
- `CacheSafety`
- `Infrastructure`
- `OidcProvider`

OIDC 身份状态增强说明：

- `status_version` 由 Cloud 后端确定性计算，不使用模糊 updated-at。
- AICopilot 状态轮询使用 `/api/v1/ai/identity/users/{cloudUserId}/status?tenantId=default`。
- 生产环境的 AI service account token 必须通过部署 secret 注入 AICopilot，不写入 Cloud 配置、前端或仓库。
- 日志和审计不得记录 service account token、OIDC code、id_token、access_token、Cloud session 或密码。

说明：

- `HttpApi` 仍然是唯一业务 API 宿主
- 不因为引入 Gateway 而打散现有分层
- 当前控制器内部仍是 `human/*` 与 `edge/*`，bootstrap 的正式外部契约先在 Gateway 层收口

### DataWorker

路径：

- `src/hosts/IIoT.DataWorker/appsettings.json`

保留 section：

- `DistributedLock`
- `Infrastructure`

## 基础设施运行时配置

关键 section 与归属如下：

| Section | 归属 | 用途 | 是否允许入仓 |
| --- | --- | --- | --- |
| `DistributedLock` | HttpApi / DataWorker | 锁租期、续期节奏 | 允许安全默认值 |
| `RateLimiting` | HttpApi | 登录、bootstrap、edge 上传限流 | 允许安全默认值 |
| `ForwardedHeaders` | HttpApi | 受信任代理来源 | 允许结构，正式网段走环境覆盖 |
| `CacheSafety` | Infrastructure / HttpApi | FusionCache fail-safe 窗口 | 允许安全默认值 |
| `Infrastructure:Postgres` | HttpApi / DataWorker / MigrationWorkApp | 数据库命令超时、EF retry 开关 | 允许安全默认值 |
| `Infrastructure:EventBus` | HttpApi / DataWorker | MassTransit retry、prefetch、启动超时、endpoint prefix | 允许安全默认值 |
| `PermissionCache` | EntityFrameworkCore | 权限缓存过期时间 | 允许安全默认值 |

说明：

- `iiot-db`、`eventbus` 这类连接资源名属于代码常量，不属于运行时配置，不放进 `appsettings`。
- 队列语义名例如 `iiot-device-logs` 继续保留在代码常量中；只有运行时策略值和可变前缀进入 `Infrastructure:EventBus`。
- 表名、列名、索引名、`create_hypertable(...)` 等数据库 schema 名称继续留在迁移与初始化代码中，不配置化。

## 部署模板配置

部署模板统一看这里：

- `src/hosts/IIoT.AppHost/aspirate-output/.env.example`
- `src/hosts/IIoT.AppHost/aspirate-output/docker-compose.yaml`
- `src/hosts/IIoT.AppHost/aspirate-output/nginx.conf`
- `src/hosts/IIoT.AppHost/aspirate-output/build-push.ps1`
- `src/hosts/IIoT.AppHost/aspirate-output/deploy.ps1`
- `src/hosts/IIoT.AppHost/aspirate-output/部署操作手册.txt`

### `.env.example`

这是部署参数样例的唯一来源。当前标准键包括：

- `IIOT_REGISTRY`
- `IIOT_HTTPAPI_IMAGE`
- `IIOT_GATEWAY_IMAGE`
- `IIOT_WEB_IMAGE`
- `IIOT_MIGRATION_IMAGE`
- `IIOT_DATAWORKER_IMAGE`
- `DEPLOY_HOST`
- `DEPLOY_USER`
- `DEPLOY_PORT`
- `STACK_NAME`
- `DEPLOY_DIR`
- `PUBLIC_BASE_URL`
- `PG_PASSWORD`
- `RABBITMQ_DEFAULT_USER`
- `RABBITMQ_DEFAULT_PASS`
- `ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN`
- `ASPIRE_DASHBOARD_OTLP_PRIMARYAPIKEY`
- `GATEWAY_HTTP_PORT`
- `BOOTSTRAP_AUTH_REQUIRE_SECRET`
- `FORWARDED_HEADERS_*`

当前 `PUBLIC_BASE_URL` 统一按 HTTP + IP 语义示例，例如：

`http://10.0.0.15`

### `docker-compose.yaml`

规则：

- `nginx-gateway` 对外暴露 HTTP 80 端口
- `iiot-gateway` 作为 API 主入口容器
- `iiot-httpapi` 不再由 Nginx 直接暴露
- 前端生产 API 地址统一来自 `${PUBLIC_BASE_URL}/api`

### `nginx.conf`

规则：

- `/` -> `iiot-web`
- `/api/` -> `iiot-gateway`
- Nginx 只负责最外层转发、安全头、基础限流、静态资源
- 本阶段不做 TLS 与证书管理

## 本地开发 / 测试专用配置

以下内容可以留在仓库中，但默认只用于开发、调试或测试：

- `launchSettings.json`
- `.http` 调试文件
- 测试工程中的 `localhost`、测试连接串、测试常量
- 文档中的示例地址和占位值

规则：

- 不允许放真实密码、真实公网地址、真实注册表地址
- 示例值统一使用 `example.com`、`registry.example.com`、`change-me-*`、`10.0.0.15` 这类占位语义

## 禁止入仓的内容

以下内容必须通过用户机密、环境变量、Aspire 参数或部署时的 `.env` 提供：

- 数据库真实密码
- RabbitMQ 真实密码
- Dashboard browser token / API key
- 真实注册表地址与登录凭据
- 真实部署主机名 / 公网域名 / 证书私钥
- 生成态文件，例如 `aspirate-state.json`

## 修改入口建议

- 想改入口拓扑：先看 `IIoT.Gateway/appsettings.json` 与 `nginx.conf`
- 想改对外访问地址：先看 `PUBLIC_BASE_URL`
- 想改宿主编排：先看 `IIoT.AppHost/AppHost.cs`
- 想改部署流程：先看 `build-push.ps1` / `deploy.ps1`
- 想确认某个配置是否允许入仓：先看本文档，再看 `ConfigurationGuardTests`

## Shared Boundaries

- IIoT.Services.Contracts only carries contracts, events, request markers, and shared protocol constants.
- IIoT.Services.CrossCutting only carries behaviors, attributes, exceptions, serialization, caching helpers, and MediatR registration glue.
