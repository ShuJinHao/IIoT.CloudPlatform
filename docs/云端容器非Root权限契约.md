# 云端容器非Root权限契约

本文档约束 `IIoT.CloudPlatform` 生产容器切到非 root 运行的权限方案。当前文档是 C-06 的执行契约；代码和发布前门禁已落地，生产关闭仍必须通过真实权限探针和容器启动验收。

## 1. 当前结论

- 不能直接在所有 Dockerfile 里加 `USER` 后宣布完成。
- `iiot-httpapi` 已切换固定非 root 用户，但它需要读取 `/app/certs/cloud-oidc-signing.pfx`，并需要写 `/app/edge-updates`；生产发布前必须由 readiness 探针验证宿主目录权限。
- `iiot-gateway`、`iiot-dataworker`、`iiot-migration` 已创建固定非 root 用户并设置 `USER ${APP_UID}:${APP_GID}`；它们主要需要读取配置、连接外部服务和写 `/app/logs`。
- 文件日志是非阻断能力：`/app/logs` 不可写时，服务必须继续通过 console/Seq 输出日志，不能因为文件日志目录权限导致启动 fatal。
- `iiot-web` 和 `nginx-gateway` 已改为容器内监听 `8080`；外部 HTTP 入口仍由 compose 映射 `GATEWAY_HTTP_PORT` 提供。
- `iiot-web` 已在镜像内设置 `USER 101:101`；`nginx-gateway` 已由 compose 设置 `NGINX_UID/GID`，并把 nginx pid/temp 目录收敛到 `/tmp`。
- `ensure-oidc-signing-cert.sh` 会校验既有 PFX 是否可被目标容器 UID/GID 读取且不 world-readable；生成新 PFX 时，会在部署用户不是目标容器 UID 的情况下尝试把文件 group 改为 `CLOUD_CONTAINER_GID` 并设置 `640`，保证容器可读但不 world-readable；无法授权 group read 时必须 fail-fast，交给运维修 owner/group/mode。新生成文件授权失败时必须删除，避免下次运行被“文件已存在”短路。

## 2. 生产关闭前必须确定的输入

生产验收和关闭 C-06 前必须明确：

- 宿主部署用户 UID/GID。
- 计划使用的容器 UID/GID。
- `OIDC_PROVIDER_CERTS_DIR` 的 owner、group、mode，以及 PFX 文件 owner、group、mode。
- `EDGE_UPDATES_DIR` 的 owner、group、mode，以及既有 `installers`、`plugins`、`velopack` 子目录权限。
- `httpapi-logs`、`dataworker-logs` 命名卷的初始化 owner、group、mode；该项是诊断信息，不得因文件日志不可写阻断应用启动。
- `iiot-web` 和 `nginx-gateway` 在生产镜像/compose 下的真实启动结果。

没有这些输入和真实启动验收，不得把 C-06 生产状态关闭。

## 3. 目标权限模型

推荐模型：

- 后端 .NET 容器使用固定业务用户，例如 UID/GID `10001:10001`，用户名 `iiot`.
- `iiot-httpapi` 的 `/app/certs` 只读挂载；PFX 文件只允许容器用户或容器用户所在 group 读取，禁止 world-readable。
- 既有或新生成的 OIDC PFX 应由 `ensure-oidc-signing-cert.sh` 按目标容器 UID/GID 校验最小可读权限：部署用户 UID 等于容器 UID 时使用 `600`，否则使用容器 GID group-read 的 `640`；不得为了通过 readiness 改成 world-readable。
- `iiot-httpapi` 的 `/app/edge-updates` 可写挂载；只给容器用户或容器发布 group 写权限。
- `iiot-httpapi` 的 `/app/edge-updates/installers`、`/app/edge-updates/plugins`、`/app/edge-updates/velopack` 若已存在，必须同样可被容器用户或容器发布 group 写入，且不得 world-writable；若尚不存在，探针只输出 missing 诊断，由可写根目录负责运行时创建。
- `iiot-gateway`、`iiot-dataworker`、`iiot-migration` 不得拥有 `/app/edge-updates` 写权限。
- `/app/logs` 可写时启用滚动文件日志；不可写时保留 console/Seq，不阻断启动。
- `iiot-web` 和 `nginx-gateway` 不直接使用 root 绑定 80；已监听非特权端口 `8080` 并由 compose 端口映射对外提供 HTTP。

## 4. 剩余执行顺序

1. 在生产服务器运行生产权限探针 `deploy/scripts/check-container-nonroot-readiness.sh`，验证 PFX 可读、Edge 更新目录可写、nginx 非特权端口配置未回退，并输出 `httpapi-logs` / `dataworker-logs` 命名卷诊断。
2. 调整宿主目录 owner/group/mode，不在容器启动时用 root chown。
3. `iiot-httpapi` Dockerfile 已创建非 root 用户并切换 `USER`；后续不得回退。
4. compose 明确运行 UID/GID 或使用镜像内固定用户，不允许 root 回退默认值。
5. 保持 nginx 容器内 `8080` 和非 root 用户设置，并同步 nginx health、compose 和文档。
6. 运行 `docker compose config -q`、容器启动验收、`/internal/healthz`、Edge installer/plugin 真实上传后的期望版本 catalog gate、OIDC discovery/JWKS、显式真实 OIDC token gate、DataWorker healthcheck 和 migration 干跑。

## 5. 验收条件

C-06 只有同时满足以下条件才算完成：

- 后端 Dockerfile 明确创建非 root 用户并设置 `USER`；当前已覆盖 `iiot-httpapi`、`iiot-gateway`、`iiot-dataworker`、`iiot-migration`。
- web/nginx 容器不再靠 root 绑定容器内 `80`，并且内部监听保持 `8080`。
- compose 没有把应用服务 `user` 回退成 `0` 或 `root`.
- `pre-deploy-check.sh` 已调用 readiness 探针验证 OIDC PFX 权限和 Edge 更新目录权限。
- `iiot-httpapi` 能读取 OIDC PFX、写 Edge release staging/final 目录，并通过 `/internal/healthz`、OIDC discovery 和 JWKS。
- `/app/logs` 不可写时应用仍能启动，并在 stderr 输出文件日志禁用原因。
- readiness 探针必须输出 `nonroot_readiness_log_volume_httpapi_logs` 和 `nonroot_readiness_log_volume_dataworker_logs`，用于生产验收判断日志卷是否已创建、宿主侧是否可检查、是否可被容器 UID/GID 写入；该诊断不得替代应用启动验收。
- C-06 生产关闭时必须先通过 Cloud 受控发布 API 上传真实 Edge installer bundle 和 plugin package，再以 `POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=1`、`POST_DEPLOY_EDGE_EXPECTED_VERSION`、`POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID` 和 `POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION` 运行 `./scripts/post-deploy-check.sh` 或等价命令，触发期望 host/plugin 版本、public catalog、installer artifact、installer stub、Velopack `RELEASES` / `.nupkg` 静态下载验收；默认 post-deploy 冒烟跳过该 gate 不足以关闭 C-06。
- `./scripts/post-deploy-check.sh` 和 `./scripts/ops-check.sh` 必须把 `iiot-dataworker` 的 Docker healthcheck 纳入默认门禁；容器缺失 healthcheck，或仅处于 running 但 health 为 `starting` / `unhealthy` 时，不得关闭 C-06。
- `pre-deploy-check.sh` 的旧当前版本检查可以默认设置 `REQUIRE_DATAWORKER_HEALTHCHECK=0`，用于从未带 healthcheck 的旧 DataWorker 镜像升级到修复版本；该兼容路径不得作为 C-06 关闭证据，发布后和日常 `ops-check.sh` 仍必须严格要求 DataWorker healthcheck。
- Edge installer bundle 上传、plugin package 上传、期望 host/plugin 版本 catalog 验证、public download catalog、Velopack 静态下载均通过。
- OIDC token 发行验证必须通过 `POST_DEPLOY_VERIFY_OIDC_TOKEN=1 ./scripts/post-deploy-check.sh` 或等价命令执行，并通过 0600 类私有文件提供真实授权码流程产生的一次性 authorization code、匹配的 PKCE verifier 和 redirect URI；通用 post-deploy 默认只验证 discovery/JWKS，不得伪造 token 凭据，也不得把 code/verifier 放入日志、文档、shell history、进程环境或 curl 进程参数。
- `iiot-migration` 能在非 root 下完成数据库迁移和 OIDC client seed。

## 6. 权限探针

在生产服务器 `deploy` 目录准备真实 `.env` 后运行：

```bash
CLOUD_CONTAINER_UID=10001 CLOUD_CONTAINER_GID=10001 sh ./scripts/check-container-nonroot-readiness.sh
```

探针既用于手工判定权限事实，也由 `pre-deploy-check.sh` 在生产发布前调用；它不改变运行时配置。它会检查：

- 容器 UID/GID 不能是 `0`.
- `OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH` 必须位于 `/app/certs`.
- 宿主 PFX 文件必须存在，且能被目标容器 UID/GID 读取。
- PFX 文件不得 world-readable。
- `EDGE_UPDATES_DIR` 必须存在，且能被目标容器 UID/GID 写入。
- `EDGE_UPDATES_DIR` 不得 world-writable。
- 既有 `EDGE_UPDATES_DIR/installers`、`EDGE_UPDATES_DIR/plugins`、`EDGE_UPDATES_DIR/velopack` 子目录必须能被目标容器 UID/GID 写入，且不得 world-writable；缺失子目录输出 `nonroot_readiness_edge_updates_subdir_<name>=missing`，不阻断空发布目录；权限不满足时输出 `nonroot_readiness_edge_updates_subdir_<name>=failed` 并阻断发布。
- 当前 nginx compose 或 nginx 配置恢复容器内 `80` 时必须 fail-fast；readiness 还必须正向确认 nginx 监听、`iiot-web` 反代和 compose 端口目标都保持 `8080`，当前配置应输出 `nonroot_readiness_nginx_internal_port=8080`。
- `httpapi-logs` 和 `dataworker-logs` 命名卷状态；卷未创建、Docker Desktop 宿主侧 mountpoint 不可访问或不可写，只输出诊断，不设置阻断失败，因为文件日志已经是非阻断能力。

## 7. 当前阻塞

当前阻塞不是“不知道怎么写 Dockerfile”，而是缺少生产宿主权限事实。直接切换会产生以下风险：

- 既有 PFX 文件可能仍是部署用户 `600`，固定容器 UID 无法读取；新生成 PFX 已由脚本尝试设置容器 GID group-read，但生产仍必须由 readiness 验证真实权限。
- `EDGE_UPDATES_DIR` 可能由历史 root/runner 写入，非 root HttpApi 不能写 staging、installer、plugin 和 velopack 文件。
- migration one-shot 若继承错误用户，可能启动失败但不易在 build 阶段发现。

因此 C-06 的代码和发布前门禁可以视为已落地，但生产状态必须保持“待真实验收关闭”，直到生产 UID/GID、`iiot-httpapi` 证书/Edge 更新目录权限方案明确，并经过真实容器启动验证。

## 8. Canonical 容器非 root 原子规则正文

本节是 Cloud 容器 non-root Rule ID 的唯一原子正文区；前文保留权限模型、探针和生产验收细节，不生成第二份 Rule ID 正文。

<a id="cloud-container-nonroot-001"></a>
### CLOUD-CONTAINER-NONROOT-001

Cloud 根的 root-owned 漂移不得被误写成整机通用故障。

<a id="cloud-container-nonroot-002"></a>
### CLOUD-CONTAINER-NONROOT-002

root 应急路径写入的 release state / cert 文件必须恢复到标准 non-root + 容器可读口径。

<a id="cloud-container-nonroot-003"></a>
### CLOUD-CONTAINER-NONROOT-003

部署文档必须拆成“长期模板/规则”和“当前生产现场口径”两层。

<a id="cloud-container-nonroot-004"></a>
### CLOUD-CONTAINER-NONROOT-004

root 应急写入恢复 owner/mode 后必须重跑标准 non-root readiness，通过前不得收口。

<a id="cloud-container-nonroot-005"></a>
### CLOUD-CONTAINER-NONROOT-005

改 deploy/默认值/当前现场目录时必须同步更新顶层入口、项目部署文档和项目规则。

<a id="cloud-container-nonroot-006"></a>
### CLOUD-CONTAINER-NONROOT-006

非 root nginx 的 pid 和临时目录必须收敛到容器用户可写的 /tmp 路径。

<a id="cloud-container-nonroot-007"></a>
### CLOUD-CONTAINER-NONROOT-007

pre-deploy 的兼容跳过只适用于更新前旧版本检查，不能作为 C-06 关闭证据。

<a id="cloud-container-nonroot-008"></a>
### CLOUD-CONTAINER-NONROOT-008

Cloud post-deploy 必须严格要求 DataWorker Docker healthcheck 通过。

<a id="cloud-container-nonroot-009"></a>
### CLOUD-CONTAINER-NONROOT-009

C-06 关闭和日常运维检查不得只看 DataWorker running，必须定义并通过 Docker healthcheck。

<a id="cloud-container-nonroot-010"></a>
### CLOUD-CONTAINER-NONROOT-010

C-06 readiness 必须正向证明 nginx 内部端口保持 8080，并对任何容器内 80 回退 fail-fast。

<a id="cloud-container-nonroot-011"></a>
### CLOUD-CONTAINER-NONROOT-011

Edge 上传限流变量不能为 0、非数字或超过 nginx edge_upload_limit 基线 12000/min。

<a id="cloud-container-nonroot-012"></a>
### CLOUD-CONTAINER-NONROOT-012

如需更大值，必须同步调整 nginx 与应用限流契约，而不能只改 .env。

<a id="cloud-container-nonroot-013"></a>
### CLOUD-CONTAINER-NONROOT-013

既有或新生成 OIDC PFX 必须按目标容器 UID/GID 满足最小可读权限。

<a id="cloud-container-nonroot-014"></a>
### CLOUD-CONTAINER-NONROOT-014

Cloud non-root readiness 对不存在的 Edge updates 子目录必须输出可区分的空目录诊断。

<a id="cloud-container-nonroot-015"></a>
### CLOUD-CONTAINER-NONROOT-015

nginx 容器必须使用非特权内部端口承载 HTTP，iiot-web / nginx-gateway 不得靠容器内 80 或 root 绑定低端口运行。

<a id="cloud-container-nonroot-016"></a>
### CLOUD-CONTAINER-NONROOT-016

Cloud 生产发布前必须运行容器非 root readiness 探针。

<a id="cloud-container-nonroot-017"></a>
### CLOUD-CONTAINER-NONROOT-017

Cloud MigrationWorkApp 的运行目录和持久化路径必须满足目标 non-root UID/GID 的最小读写权限。

<a id="cloud-container-nonroot-018"></a>
### CLOUD-CONTAINER-NONROOT-018

Cloud non-root readiness 对已存在但 owner/mode 不满足的 Edge updates 子目录必须阻断发布。

<a id="cloud-container-nonroot-020"></a>
### CLOUD-CONTAINER-NONROOT-020

Cloud EDGE_UPDATES_DIR 及其已有发布子目录不得 world-writable。

<a id="cloud-container-nonroot-021"></a>
### CLOUD-CONTAINER-NONROOT-021

Cloud 日常 ops-check 必须严格要求 DataWorker Docker healthcheck 通过。
