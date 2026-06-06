# Cloud 安全凭据收口记录 - 2026-05-28

## 本批目标

- 只处理 `IIoT.CloudPlatform` 的测试凭据和部署凭据防护。
- 消除 E2E 测试种子管理员密码与本机部署 `.env` 管理员密码同值的问题。
- 阻断已知弱 JWT secret 和已知弱管理员密码继续用于部署。

## 实际改动

- `IIoTAppFixture` 的 `SeedAdminPassword` 和 `TestJwtSecret` 改为测试进程启动时生成的随机测试值。
- `aspirate-output/deploy.ps1` 在部署前拒绝已知弱 JWT secret 和已知弱管理员密码。
- 本机 ignored 文件 `src/hosts/IIoT.AppHost/aspirate-output/.env` 已完成本地轮换：
  - `JWTSETTINGS__SECRET` 使用 `openssl rand -hex 32` 生成。
  - `SEED_ADMIN_PASSWORD` 使用安全前缀、`openssl rand -hex 16` 随机段和符号组合生成。
- `ConfigurationGuardTests` 增加守卫，防止测试 fixture、部署脚本和本机 `.env` 回退到上述弱值。

## 未修改范围

- 未修改 `AICopilot/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 Cloud 账号、权限、设备、生产数据、bootstrap、上传、缓存或补偿业务链路。
- 未修改公开 API、DTO、数据库实体、迁移、Docker compose 服务结构或外部网关端口。
- 未删除数据库、volume 或任何生产数据。

## 验证结果

- `dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --no-restore --filter "FullyQualifiedName~ConfigurationGuardTests"`
  - 结果：通过 52/52。
- `dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --no-restore`
  - 结果：失败 42、通过 57、总计 99；失败均为 Aspire 启动前 `An item with the same key has already been added. Key: http_proxy`，未进入业务断言。
  - 复测：临时清理 proxy 环境变量后仍失败同一错误；安全凭据批次执行时本地工作树已有 `src/hosts/IIoT.AppHost/Properties/launchSettings.json` proxy 相关脏改动，该批未修改该文件。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：安全凭据批次 tracked diff 包含 4 个文件；当时同时显示该批前已存在的 `src/hosts/IIoT.AppHost/Properties/launchSettings.json` proxy 脏改动。
- `rg --no-ignore -n "<known weak admin password>|<known weak jwt secret>" .`
  - 结果：只命中 `deploy.ps1` denylist 和 `ConfigurationGuardTests` 测试断言；未命中本机 `.env` 实际配置值。

## 剩余风险

- 本批只轮换当前本机 ignored `.env`。已经部署到服务器的 `.env` 或外部 secret store 需要按运维流程同步轮换。
- `deploy.ps1` 已阻断已知弱值，但无法判断所有低熵自定义值；后续可单独增加最小长度和复杂度检查。

## 下一阶段进入条件

- 当前 CloudPlatform 测试和部署 guard 验证通过。
- 如需处理服务器已部署凭据，单独开部署运维批次，先确认目标环境、数据保护要求和回滚方式。

## E2E Aspire Proxy 稳定化补充 - 2026-05-28

### 本批目标

- 只处理 `IIoT.CloudPlatform` 的 E2E 测试启动环境。
- 消除 Aspire Testing 在构建 AppHost 前因大小写 proxy 环境变量重复导致的 `http_proxy` 键冲突。
- 保持 Cloud 业务逻辑、数据库结构、公开 API/DTO、部署 compose 结构和端口不变。

### 实际改动

- `IIoTAppFixture` 在创建 `DistributedApplicationTestingBuilder` 前临时清理 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY` 及对应小写变量。
- `IIoTAppFixture` 只设置一个规范化 `NO_PROXY`，覆盖 localhost、loopback、`host.docker.internal`、`.local` 和 link-local 网段，并在 `DisposeAsync` 恢复原环境变量。
- `IIoT.AppHost` 的 `launchSettings.json` 移除 proxy 环境变量，保留 Aspire dashboard/resource endpoint 和 `ASPIRE_ALLOW_UNSECURED_TRANSPORT`。
- `ConfigurationGuardTests` 增加守卫，确认 launch profile 不再定义 proxy 变量，并确认 fixture 在 Aspire builder 创建前执行 proxy 清理。

### 验证结果

- `dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --no-restore --filter "FullyQualifiedName~ConfigurationGuardTests"`
  - 结果：通过 54/54。
- `dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --no-restore`
  - 结果：通过 101/101，用时 29 分 29 秒。
  - 结论：`http_proxy` 重复键不再阻断 Aspire E2E 启动。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：tracked diff 包含 `aspirate-output/deploy.ps1`、`ConfigurationGuardTests.cs`、`IIoTAppFixture.cs`。
  - 说明：`launchSettings.json` 的 proxy 脏改动已收敛回基线；本阶段记录为新 untracked 文档。
- `rg -n "HTTP_PROXY|http_proxy|HTTPS_PROXY|https_proxy|ALL_PROXY|all_proxy" src/hosts/IIoT.AppHost/Properties/launchSettings.json src/tests/IIoT.EndToEndTests`
  - 结果：仅命中 `IIoTAppFixture.cs` 的清理列表和 `ConfigurationGuardTests.cs` 的守卫断言；`launchSettings.json` 无命中。
