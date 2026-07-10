# IIoT Cloud Harbor CICD And Private Server Deploy

本目录是 `IIoT.CloudPlatform` 镜像构建和旧事务维护实现目录。Cloud 日常应用发布统一从工作区根 `deploy/Deploy.ps1` 发起：入口从 `origin/main` tip 创建隔离快照，调用本目录 `build-and-push.sh`，再以一次 SSH 请求稳定服务器 Runner。GitHub Actions 只保留 CI 留痕和灾备手动入口。三项目口径见 [上传部署总览](../../docs/上传部署总览.md)。

> 当前状态（2026-07-10）：新日常入口/Runner 已通过 fake 远端仓库、脏本地工作树、不可变 OCI、一次 SSH、失败回滚和不重建续传回归；旧事务链 33/33、`DeploymentGuardTests` 20/20、名称含 `Deploy` 的门禁 39/39 仍保留。没有连接真实 Harbor/SSH/生产容器，隔离回归不得冒充生产验收。

日常标准入口示例：

```powershell
pwsh ./deploy/Deploy.ps1 -Target Cloud -InstallRunner # 仅首次或 Runner 升级
pwsh ./deploy/Deploy.ps1 -Target Cloud -Doctor
pwsh ./deploy/Deploy.ps1 -Target Cloud -Services web -DryRun
pwsh ./deploy/Deploy.ps1 -Target Cloud -Services httpapi,dataworker -Deploy
```

## 部署口径

- 当前目标是 single-machine production starter。
- 生产版本统一使用 `release_tag = sha-*`，`latest` 不能作为生产应用版本。
- 多 agent 并行部署只按 [上传部署总览](../../docs/上传部署总览.md) 的“多 agent 并行部署”执行；Cloud agent 只负责 Cloud 镜像、Cloud deploy 和 Cloud 验证。
- Cloud 应用镜像不保留历史版本；Harbor 和服务器本机只保留当前生产正在运行的 `sha-*` 应用镜像。
- 日常部署必须先 push GitHub；`Deploy.ps1` 直接取配置远端 tip 建 detached worktree。本地工作树允许继续迭代，入口不会修改它，未推送改动也不会发布。
- 工作区外部日常入口是 `pwsh ./deploy/Deploy.ps1 -Target Cloud ...`；本目录 `build-and-push.sh` 只负责镜像，`local-release.sh` / `deploy-release.sh` 只保留给基础设施维护和旧链恢复。
- Compose、nginx 和服务器 scripts 不再随每次应用发布同步。它们属于部署基础设施，必须在独立维护窗口通过旧事务入口升级并重新运行 `Deploy.ps1 -InstallRunner/-Doctor`；不得让应用发布偷偷改变服务器执行代码。
- 传入 `services` 时只拉取并重启指定服务；首次部署或需要全量时必须显式传 `--all`。
- `cloud-image` / `cloud-deploy` 只保留灾备手动入口，必须输入确认词；不得在日常生产发布中等待这些 workflow。
- 单个镜像 build/push 默认 15 分钟超时，Harbor 登录/API 检查默认 2 分钟超时，SSH deploy 默认 40 分钟超时；超时必须停止并按脚本输出诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续 watch 或无限等待。
- 日常入口必须实时显示 fetch/snapshot/build/remote/backup/migration/rollout/rollback 阶段。构建完成后的远端失败优先使用 `-ResumeInvocation` 重发同一请求，不得绕开入口或重新全量构建。
- Cloud release 和 post-release cleanup 都使用带 PID、进程启动标识、release 与 phase 的 managed lock；活锁或初始化中的锁立即失败，只有能证明进程已失效的 stale lock 才自动认领并移除，不再静默等待 15 分钟。
- 操作员 `.env` 由独立严格配置锁保护；deploy、rollback 和受支持的 `scripts/update-deploy-env.sh` 从读取到最终状态晋升共用同一把锁。严格配置锁一旦存在一律 fail-closed，不得 stale 自动清理；必须先人工核对 owner/phase 证据。
- deploy/rollback 不再替换 `.env`；五个应用镜像坐标是 release-owned state，原子写入 `releases/current-images.env`，compose 以 `.env` 为操作员配置基层、以 `current-images.env` 为镜像覆盖层。直接编辑 `.env` 绕过共享锁，属于不受支持操作；受支持更新必须传入 expected SHA-256。
- self-hosted runner 仅作为灾备/历史 CI 设施时使用，仍必须是专用非 root 用户，不能用 root 跑 Actions 服务。
- 当前服务器 Docker Root Dir 固定为 `/data/iiot-platform/runtime/docker`，runner 工作目录固定在 `/data/github-runner/*`，不要把构建缓存和 runner workdir 放回系统盘。
- Docker Hub 不作为生产依赖源；compose 第三方镜像和 Web Dockerfile 的 Node/Nginx 基础镜像必须先同步到 Harbor mirror。
- Edge 客户端安装素材不进 Harbor；日常 push main 只跑 smoke，完整 GitHub 打包只在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 时执行。日常宿主快发统一从工作区根运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgeHost ...`，只改工序插件时运行 `-Target EdgePlugin ...`；顶层入口再调度 EdgeClient 项目脚本完成内网受控 HTTP 上传。生产 `stable` 不允许用 `rsync/scp` 绕过 Cloud DB、审计和保留策略。
- 生产服务器只允许 Edge `stable` 渠道；发布脚本必须拒绝并清理 `ci`、`dev`、`test` 等非 `stable` 渠道目录。
- Cloud catalog、首装、Edge 更新和公开下载的版本集合只来自 Cloud release 记录。文件系统只校验已登记 artifact 的存在性、完整性、受控路径、权限和真实下载；不得扫描目录补出未登记版本，残留文件也不得让 `Deleted` 版本重新可见。
- 本机 Docker 构建和 SSH 触发服务器部署是标准 Cloud 发布流程。
- 工作区 `deploy/server/iiot-release-runner.sh` 是日常服务器端入口；`deploy/scripts/deploy-release.sh` 和 `deploy/scripts/rollback-release.sh` 是旧事务维护/恢复入口。
- self-hosted runner 安装和权限要求见 [RUNNER.md](./RUNNER.md)。
- 运维、备份、恢复和检查细节见 [OPERATIONS.md](./OPERATIONS.md)。
- Cloud 下载中心生成 Edge 客户端 `.exe` 的上线顺序见 [EDGE_INSTALLER_GO_LIVE.md](./EDGE_INSTALLER_GO_LIVE.md)。

当前部署文档按双层口径维护：

- 长期模板/规则：描述标准 Harbor/SSH/non-root 发布链路、红线、应急条件和恢复动作，不写真实密码或真实 token。
- 当前生产现场口径：当前 Cloud 部署目录是 `/data/iiot-platform/cloud/deploy`，标准部署用户是 `github-runner`，Docker Root Dir 是 `/data/iiot-platform/runtime/docker`，Edge 发布上传默认值是 `1000 Mbps`。
- 当前与 `AICopilot` 共用同一台生产宿主机，但部署根独立：AICopilot 当前根目录是 `/srv/enterprise-ai/deploy`。两条日常 SSH 发布链路都走 `github-runner` 非 root 路径；这轮 root-owned 漂移已确认发生在 Cloud 根，不是整机通用故障。
- `certs/` 与 `releases/current-release*`、`staged-release*`、`previous-release*` 都必须对标准 non-root 部署用户可读可写；root 应急路径一旦写入这些状态，关闭任务前必须恢复 owner/mode 并重新验证标准 non-root preflight。

## 运行拓扑

入口链路固定：

```text
nginx-gateway -> iiot-gateway -> iiot-httpapi
iiot-dataworker -> RabbitMQ queues
iiot-migration -> one-shot migration/bootstrap job
```

`docker-compose.prod.yml` 的默认资源和吞吐配置按 16GB 单机生产起步值设置，并在 `.env` 中保留覆盖项。默认给 Cloud 预留约 8-10GB 容器额度，保留 OS、Docker、备份任务和同机 AICopilot 的余量；真实上线前仍以现场压测为准调整 `POSTGRES_*`、`HTTPAPI_*`、`DATAWORKER_*`、`OUTBOX_*`、`*_CONSUMER_CONCURRENCY` 和 `RATE_LIMIT_*`。

外部暴露：

- `${GATEWAY_HTTP_PORT:-80}`：产品入口。
- `/api/v1/human/*`：人工端 API。
- `/api/v1/edge/*`：边端上传 API。
- `/api/v1/machine/*`：机器身份 API；当前用于 Edge Release API key 换短期发布 JWT。
- `/api/v1/bootstrap/*`：边端 bootstrap API。
- `/api/v1/public/client-downloads/latest`：公开客户端下载目录 API，只暴露已发布通用宿主下载和插件版本 catalog。
- `/downloads`：公开客户端下载中心页面，不要求登录。
- `/edge-updates/*`：EdgeClient 自动更新包下载，以及安装素材静态只读下载。

正式 bootstrap 入口固定为：

- `/api/v1/bootstrap/device-instance`
- `/api/v1/bootstrap/edge-login`
- `/api/v1/bootstrap/edge-refresh`

边端 bootstrap 必须发送 `X-IIoT-Bootstrap-Secret`。旧 alias 不作为部署或联调入口，例如 `/api/v1/edge/bootstrap/device-instance`、`/api/v1/human/identity/edge-login` 必须 rejected。

只允许服务器本机访问：

- `GET /internal/healthz`
- RabbitMQ management：`127.0.0.1:${RABBITMQ_MANAGEMENT_PORT:-15672}`
- Seq：`127.0.0.1:${SEQ_HOST_PORT:-5341}`

## Harbor 镜像

Cloud 应用镜像统一推送到 Harbor，同一批版本使用同一个 `sha-*` tag。全量发布包含以下五个应用镜像；按需发布时只要求受影响服务的镜像存在：

```text
<OCI_REGISTRY>/<OCI_NAMESPACE>/iiot-httpapi:<release_tag>
<OCI_REGISTRY>/<OCI_NAMESPACE>/iiot-gateway:<release_tag>
<OCI_REGISTRY>/<OCI_NAMESPACE>/iiot-dataworker:<release_tag>
<OCI_REGISTRY>/<OCI_NAMESPACE>/iiot-migrationworkapp:<release_tag>
<OCI_REGISTRY>/<OCI_NAMESPACE>/iiot-web:<release_tag>
```

Harbor 变量：

- `OCI_REGISTRY`：Harbor 地址，必须填写真实内网 registry；`harbor.example.com` / `harbor.internal.example` 只是文档占位，脚本会拒绝。
- `OCI_NAMESPACE`：Harbor 项目/命名空间，例如 `iiot`。
- `OCI_REGISTRY_USERNAME`：Harbor 登录用户名。
- `OCI_REGISTRY_PASSWORD`：Harbor 登录密码或 robot account token。

标准生产镜像由统一入口调度 `deploy/scripts/build-and-push.sh` 构建并推送 Harbor。脚本必须显式传入 `--services httpapi,gateway,dataworker,migration,web` 的子集，或显式 `--all`；无参数直接失败，避免误全量。正式发布时统一入口为每次 run 传入私有 output 目录，`local-release.sh` 只读取同一次运行的 `cloud-built-services.txt` 和 image manifest；默认共享路径只服务单独诊断实现脚本，不得作为正式发布控制面。
构建 `web` 或 `--all` 时必须显式传入 `VITE_AICOPILOT_CHALLENGE_URL`，backend-only 构建不需要该变量；脚本会拒绝 `.example` / `internal.example` 文档域名。

`cloud-image` 只保留灾备手动入口，并要求确认词 `EMERGENCY_CLOUD_IMAGE_BUILD`；不得把它当作日常发布路径，也不得等待它超时。灾备 workflow 仍必须跑在带 `iiot-linux-prod` label 的内网 self-hosted runner 上，不能改回 `ubuntu-latest`。

应用镜像仓库只保留当前生产 `sha-*` tag。本机构建推送候选 tag 后，不立即删除当前生产 tag；稳定 Runner 健康提交后由独立定时维护任务清理旧 tag 并执行或确认 Harbor GC。cleanup/GC 失败只产生运维告警，不得把健康应用发布改写为失败。`main-latest`、`buildcache` 和 `mirror` 基础镜像 tag 不计入应用版本保留。

Cloud 发布成功且 `post-deploy-check.sh`、`ops-check.sh` 通过后，必须执行发布后清理：

- 清理 Docker/BuildKit build cache。
- 分开统计并清理 Docker 管理镜像和 containerd 管理内容，只删除未被当前容器引用的旧 Cloud 应用镜像；containerd 侧未确认 namespace/ref/lease 前不得强删。
- 删除 Harbor 旧 Cloud 应用 `sha-*` tag 后执行或确认 Harbor GC。
- 输出清理前后 `df -h /data`、`docker system df`、containerd snapshots/content 占用、Harbor registry 占用和 Edge 更新目录占用摘要。

禁止清理基础镜像 mirror、Harbor 自身镜像、数据库卷、备份、配置和 secrets。Cloud 快速回滚不再依赖本机旧镜像；需要回滚时重新构建或重新拉取目标 git sha 后部署。

禁止把 `cloud-image` 或 `cloud-deploy` 当成日常部署入口。它们只允许在本机 Docker/SSH 发布路径不可用且操作者明确选择灾备时使用，并必须带确认词。

## 第三方镜像 mirror

生产服务器 Docker Hub 不通，不能在 `.env`、compose 或 Dockerfile 默认 ARG 里使用 `nginx:...`、`redis:...`、`rabbitmq:...`、`timescale/...`、`datalust/...`、`mcr.microsoft.com/...` 这类公网源作为生产默认值。运行时第三方镜像和构建基础镜像统一推到 Harbor `mirror` 项目：

```text
<OCI_REGISTRY>/mirror/timescaledb:latest-pg17
<OCI_REGISTRY>/mirror/redis:7.4-alpine
<OCI_REGISTRY>/mirror/rabbitmq:3-management-alpine
<OCI_REGISTRY>/mirror/seq:2024.3
<OCI_REGISTRY>/mirror/dotnet-sdk:10.0.301
<OCI_REGISTRY>/mirror/dotnet-aspnet:10.0.9
<OCI_REGISTRY>/mirror/nginx:1.27-alpine
<OCI_REGISTRY>/mirror/node:22-slim
```

在能访问 Docker Hub 的机器，或已经有本地镜像缓存的机器上执行：

```sh
docker login <OCI_REGISTRY> --username <OCI_REGISTRY_USERNAME>
MIRROR_REGISTRY=<OCI_REGISTRY> MIRROR_NAMESPACE=mirror ./deploy/scripts/mirror-third-party-images.sh
```

`deploy/.env.example` 和后端 Dockerfile 默认 ARG 已指向 Harbor mirror；`pre-deploy-check.sh` 会拒绝 Docker Hub shorthand，避免服务器重建或 `docker compose pull` 时卡在外网。
`build-and-push.sh`、`mirror-third-party-images.sh`、`local-release.sh` 和 `verify-edge-installer-catalog.sh` 也会拒绝 `.example` / `internal.example` 文档域名，运行前必须替换成真实内网 registry、SSH 目标、AICopilot challenge URL 和 Cloud Gateway URL。

## Edge 安装素材

Edge 客户端产物不属于 Cloud Docker 镜像，也不进入 Harbor。当前流程分三类：

- `push main`：只跑 smoke 编译和测试，不发布安装包。
- `workflow_dispatch` 或 `edge-v*` / `v*` tag：由 GitHub hosted `windows-latest` 构建 runtime、installer artifact 和 Velopack releases，再由内网 `iiot-linux-prod` runner 把 GitHub Actions artifacts 发布到 `${EDGE_UPDATES_DIR}`，渠道固定为 `stable`。
- 日常宿主快发：操作者从工作区根运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgeHost ...`。顶层入口调度 EdgeClient 本机实现脚本完成编译、Velopack 打包、installer artifact 生成和 Cloud Human API 上传。Cloud 服务端默认限速 `1000 Mbps`、单并发、服务端审计，渠道固定为 `stable`。这是本机运维快发路径，不是 GitHub CI/CD job。生产 `stable` 不允许用 `rsync/scp`。
- 日常插件快发：只改工序插件时从工作区根运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgePlugin -ModuleId <真实ModuleId> ...`，顶层入口只上传独立插件 zip；Cloud 通过 `POST /api/v1/human/client-releases/plugin-packages` 落盘到 `${EDGE_UPDATES_DIR}/plugins/stable/<ModuleId>/<version>/` 并写插件 release。
- Edge 更新内容必须显式填写。本机快发传 `-ReleaseNotes` 或 `-ReleaseNotesPath`；正式 `workflow_dispatch` 填 `release_notes`；tag 发布必须使用带正文的 annotated tag。Cloud Human 发布接口和 host/plugin release upsert 会拒绝 `Published` 空更新内容。

GitHub 完整打包流程固定为：

```text
IIoT.EdgeClient workflow_dispatch / tag
-> windows-latest 构建 edge-installer-artifact 和 edge-velopack-releases
-> 上传 GitHub Actions artifacts
-> iiot-linux-prod 内网 self-hosted runner 下载 artifacts
-> 本地发布到 ${EDGE_UPDATES_DIR}
```

当前生产服务器 `EDGE_UPDATES_DIR=/data/iiot-platform/edge-client/edge-updates`。

CloudPlatform 不负责构建 Edge 安装素材。日常快发时，Cloud Human API 负责接收本机生成的 release bundle、校验、限速、落盘和登记 DB release 行；Nginx 仍只读提供静态下载。发布后的目录固定为：

```text
${EDGE_UPDATES_DIR}/installers/stable/1.2.0/
  IIoT.Edge.Setup.exe
  launcher/
  host/
  plugins/
  velopack/
  installer-artifact.json
${EDGE_UPDATES_DIR}/velopack/stable/
  releases.stable.json
  assets.stable.json
  *.nupkg
  *-Setup.exe
  *-Portable.zip
```

`iiot-httpapi` 以可写方式挂载 `${EDGE_UPDATES_DIR}` 到 `/app/edge-updates`，仅用于内网受控 Edge HTTP 发布；`nginx-gateway` 仍以只读方式挂载同一目录。HttpApi 通过 `EdgeInstallerArtifacts__RootPath=/app/edge-updates/installers` 读取安装素材，并通过 `EdgeInstallerArtifacts__VelopackReleasesBaseUrl=${PUBLIC_BASE_URL}/edge-updates/velopack` 返回运行时更新源。HTTP 宿主上传成功后，服务端会校验 `installer-artifact.json` v2 和独立插件 zip，从 manifest 派生并写入正式 host/plugin release 记录；公开下载目录、Edge catalog、Human catalog 和首装链路只按这些 Cloud release 记录决定版本集合，文件系统只验证已登记产物。已登记同版本插件不会被宿主发布覆盖。插件独立发布必须走 `plugin-packages`，文件存在性以落盘包为准，可见性和生命周期以 DB 状态为准。保留策略默认每个 stable/runtime/component 按 SemVer 最多保留最新 3 次，旧版本先 Archived/Deprecated，只有已归档且无设备在用的文件才会回收。登录 Cloud 后，“客户端下载中心 -> 首装下载”会按已登记版本对应的 `installer-artifact.json` v2 选择一份 `host/` 和所选 `plugins/<ModuleId>/`，把 `launcher/iiot-binding.json`、`launcher/iiot-enabled-plugins.json`、`launcher/launcher.update.json`、`plugins/<ModuleId>/iiot-plugin-binding.json` 注入本次下载的安装器 payload，并返回真正的 `.exe`。这些绑定配置不写回素材目录、不落盘到共享模板、不写日志。

`iiot-web` 是 Vite 静态构建，Cloud 左侧“打开助手”按钮需要在构建镜像时注入 AICopilot challenge URL：

```text
VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge
```

未注入时按钮会按设计置灰。

## 服务器部署目录

服务器部署目录由生产服务器 `.env`、Docker compose label 和本机发布脚本参数共同确定。当前生产服务器值为：

```text
/data/iiot-platform/cloud/deploy
```

标准流程要求服务器部署目录已经包含 `deploy/` 模板和真实 `.env`：

```text
deploy/
deploy/.env
```

真实 `.env` 不提交仓库。`deploy/.env.example` 只作为模板；服务器端 `deploy-release.sh` 只读取现有 `.env` 并按 release tag 重写应用镜像 tag，不负责生成真实密钥。GitHub `cloud-deploy` 灾备入口如仍使用 secret 注入，也不得被写成日常标准路径。

生产服务器以容器标签为准确认真实部署目录，不要按旧路径猜：

```sh
docker inspect deploy-iiot-httpapi-1 \
  --format 'working={{index .Config.Labels "com.docker.compose.project.working_dir"}} config={{index .Config.Labels "com.docker.compose.project.config_files"}} project={{index .Config.Labels "com.docker.compose.project"}}'
```

2026-06-22 现场校准（不是模板默认值）：`jms.hdc-group.cn` / `10.98.90.154` 的 Cloud compose 工作目录为 `/data/iiot-platform/cloud/deploy`，Docker Root Dir 为 `/data/iiot-platform/runtime/docker`，Edge 更新素材目录为 `/data/iiot-platform/edge-client/edge-updates`。如果服务器目录和本文不一致，先用上面的标签命令对齐真实目录，再修改文档。

首次部署或明确允许清空测试环境时，可以删除旧 stack、旧 compose 容器和旧卷，再按新版 compose 重新启动。例子：

```sh
docker stack ls
docker stack rm <old-stack-name>
docker compose -f /data/iiot-platform/cloud/deploy/docker-compose.prod.yml down -v
docker volume ls | grep -E 'iiot|postgres|rabbitmq|seq'
```

只在确认旧数据不需要保留时执行清空动作。

## .env 要点

应用镜像坐标必须指向 Harbor 仓库，tag 可以先写任意 `sha-*`；全量发布时发布脚本会按传入 release tag 重写五个应用镜像 tag，按需发布时只重写所选服务对应的镜像 tag：

```text
IIOT_HTTPAPI_IMAGE=harbor.example.com/iiot/iiot-httpapi:sha-0123456789abcdef
IIOT_GATEWAY_IMAGE=harbor.example.com/iiot/iiot-gateway:sha-0123456789abcdef
IIOT_DATAWORKER_IMAGE=harbor.example.com/iiot/iiot-dataworker:sha-0123456789abcdef
IIOT_MIGRATION_IMAGE=harbor.example.com/iiot/iiot-migrationworkapp:sha-0123456789abcdef
IIOT_WEB_IMAGE=harbor.example.com/iiot/iiot-web:sha-0123456789abcdef
```

`harbor.example.com` 只是文档占位，生产值必须带真实 Harbor/内部 registry，不能写成 `iiot-httpapi:sha-*` 这类无 registry 的短镜像名。`pre-deploy-check.sh` 会拒绝仍指向 `.example` registry 的应用镜像或基础设施镜像，避免复制模板后等到 pull 阶段才失败。

这些值必须替换为生产真实值：

- `PUBLIC_BASE_URL`
- `PG_PASSWORD`
- `RABBITMQ_DEFAULT_PASS`
- `JWTSETTINGS__SECRET`
- `SEQ_ADMIN_PASSWORD`
- `SEED_ADMIN_NO`
- `SEQ_API_KEY`（启用 Seq ingestion key 时）

`pre-deploy-check.sh` 会拒绝空 secret、模板 secret、已知弱 secret 和过短 secret。`JWTSETTINGS__SECRET` 至少 32 字符，`PG_PASSWORD`、`RABBITMQ_DEFAULT_PASS`、`SEQ_ADMIN_PASSWORD`、`SEED_ADMIN_PASSWORD` 至少 12 字符。

`RATE_LIMIT_CAPACITY_UPLOAD_*`、`RATE_LIMIT_DEVICE_LOG_UPLOAD_*`、`RATE_LIMIT_PASS_STATION_UPLOAD_*` 必须是正整数，且单项不得超过 nginx `edge_upload_limit` 基线 12000 次/分钟；超过该值应先压测并调整 nginx 与应用限流契约，不能只改 `.env` 放大应用侧限流。

### 固定 Cloud 管理员账号

当前首部署管理员工号固定为：

```text
SEED_ADMIN_NO=101650
```

管理员密码由 GitHub secret `SEED_ADMIN_PASSWORD` 单独管理，不放入 `DEPLOY_ENV_FILE`、仓库、文档或日志。该密码是操作者约定的固定生产登录密码，不允许部署脚本、CI、AI 或临时排障流程自动随机化、覆盖或猜测。

`iiot-migration` 的播种规则是：

- 数据库中不存在任何 `Admin` 用户时，使用 `SEED_ADMIN_NO` 和 GitHub secret `SEED_ADMIN_PASSWORD` 创建首个管理员。
- 已存在 `Admin` 用户时，直接跳过管理员播种，不重置、不覆盖现有密码。
- `SEED_ADMIN_PASSWORD` 只用于首个管理员创建和显式管理员修复；常规部署不会改管理员密码。

Cloud 管理员密码和 Edge Launcher 本地样例账号密码是两套凭据。`launcher.accounts.sample.json` 里的 `101650` 只用于本地启动器样例，不是 Cloud 登录密码来源。Cloud 登录失败时，不得从样例文件、测试常量或历史弱密码推断生产密码；应按 `OPERATIONS.md` 的“Cloud 管理员登录排查”处理。

这些值通常保持模板默认：

- `GATEWAY_HTTP_PORT`
- `SEQ_HOST_PORT`
- `RABBITMQ_MANAGEMENT_PORT`
- `EDGE_UPDATES_DIR`
- `BACKUP_RETENTION_DAYS`
- `BACKUP_MAX_AGE_HOURS`
- `BACKUP_VERIFY_MAX_AGE_DAYS`

`PUBLIC_BASE_URL` 必须是 origin，不要以 `/` 结尾，例如：

```text
PUBLIC_BASE_URL=http://<cloud-host>:81
```

内网 HTTP OIDC 要显式打开，且 Cloud issuer、AICopilot callback 和 logout redirect 必须使用 loopback 或 RFC1918 私网 IPv4；不要使用 `cloud.internal.example` / `aicopilot.internal.example` 这类模板域名，也不要使用普通内网 DNS 域名：

```text
ALLOW_INTRANET_HTTP_OIDC=true
OIDC_PROVIDER_ISSUER=http://<cloud-private-ip>:81
AICOPILOT_OIDC_REDIRECT_URI=http://<aicopilot-private-ip>:82/api/identity/cloud-oidc/callback
AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI=http://<aicopilot-private-ip>:82/login
OIDC_PROVIDER_CERTS_DIR=/data/iiot-platform/cloud/deploy/certs
OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH=/app/certs/cloud-oidc-signing.pfx
OIDC_PROVIDER_SIGNING_CERTIFICATE_PASSWORD=
VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge
```

`AICOPILOT_OIDC_REDIRECT_URI` 改动后必须让 `iiot-migration` 重新跑一次，重新 seed OIDC client，否则 Cloud 仍会使用旧回调地址。

OIDC 签名证书是 Cloud 本地 token 签名证书，不是公网 HTTPS 证书，不需要购买。`pre-deploy-check.sh` 会先完成 secret、公开地址、OIDC HTTP 边界、镜像和 compose 等无副作用校验，再调用 `ensure-oidc-signing-cert.sh`。该脚本会校验既有 PFX 是否可被目标容器 UID/GID 读取且不 world-readable；没有 PFX 时自动生成持久化自签名 PFX。部署用户 UID 等于容器 UID 时使用 `600`，否则尝试 `chgrp CLOUD_CONTAINER_GID` 并设置 `640`。前提是该目录存在且专用部署用户可写：

```sh
sudo mkdir -p /data/iiot-platform/cloud/deploy/certs
sudo chown -R github-runner:github-runner /data/iiot-platform/cloud/deploy/certs
sudo chmod 755 /data/iiot-platform/cloud/deploy/certs
```

同一轮标准 non-root 发布还要求 release state 保持可访问。`pre-deploy-check.sh` 现在会先校验：

- `deploy/releases/`、`deploy/releases/history/` 目录对标准部署用户可读、可写、可遍历。
- 已存在的 `current-release.env`、`previous-release.env`、`staged-release.env`、`current-release.summary.md`、`current-images.env` 和 `staged-images.env` 对标准部署用户可读可写。

如果之前走过 root 应急路径，必须先恢复 owner/mode，例如：

```sh
sudo chown -R github-runner:github-runner /data/iiot-platform/cloud/deploy/releases
sudo find /data/iiot-platform/cloud/deploy/releases -type d -exec chmod 755 {} +
sudo find /data/iiot-platform/cloud/deploy/releases -type f -exec chmod 600 {} +
```

## 标准发布步骤

标准路径：

1. 合并或推送到 `main`，保证 GitHub 有本次源码留痕。
2. `cloud-ci` 默认只跑快速验证：restore/build、ServiceLayer、ConfigurationGuard、部署脚本语法检查、前端 build、compose config；完整 EndToEnd 只在手动 `workflow_dispatch` 勾选时运行。
3. 基础设施维护才运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target Cloud ...`；日常应用发布使用 `Deploy.ps1`，不进入本节 support transaction。旧入口仍要求 clean/pushed candidate 和显式 plan，用于 Compose/nginx/scripts 升级取证。
4. 根入口为正式运行注入 entrypoint marker、唯一 invocation id、完整 expected SHA 和 plan digest；`local-release.sh` 缺任一项即在 build/SSH 前失败，并强校验 expected SHA 等于 detached 固定快照 HEAD。
5. 旧 `local-release.sh` 在基础设施维护窗口校验 support 白名单并完成 staging；日常 `Deploy.ps1` 不安装任何远端 support files。
6. 每次 invocation 独占 services 文件和 image manifest；manifest 绑定 invocation、expected SHA、plan digest、release tag、services 和每个已构建镜像的 OCI digest。dry-run 不得覆盖正式清单。
7. support 包和 image manifest 一起进入远端 invocation staging；远端先校验两份 SHA-256，再由 `workspace-release-transaction.sh` 扫描未完成 transaction marker、lock、staging 和 recovery 证据。任一孤儿状态都原子写入 durable blocked 并返回 78，不得用 stale-lock 清理掩盖 SIGKILL/OOM/重启中断。通过扫描后才获取绑定 invocation/plan 的 release lock，并在 support 发生任何变更前原子写入 transaction marker。
8. 父事务进程持锁安装 support，installer 和 `deploy-release.sh` 都在独立 session/process group 中从 `env -i` 白名单环境启动。父进程收到 HUP/INT/TERM 时向整个子进程组转发，宽限内未退出就 KILL；只有确认无子孙进程后才允许恢复 support 或释锁。current state 与 marker 共同证明 promotion 后，cleanup 绝不恢复旧 support；父进程在 marker 后 kill-9 留下的 backup/staging/lock 由下一次入口只做 cleanup-only。
9. support 安装先在 staging 预检，再备份新旧 allowlist 并原子替换；安装中断恢复旧 manifest/文件/缺失状态。恢复失败返回专用状态 `86`，保留锁、staging 和 recovery backup，后续入口先读取阻断证据。`.env`、`certs/`、`releases/`、`backups/` 始终不进入 support 白名单。
10. `deploy-release.sh` 强校验 release tag SHA、`DEPLOY_GIT_SHA`、expected SHA 三者一致，只消费本 invocation manifest，pull 后在 rollout 前核对 OCI digest。deploy/rollback 先获取严格操作员配置锁，在持锁期间读取和复核 `.env`，但绝不替换它；release 镜像单独经 `staged-images.env` 原子晋升到 `current-images.env`。current/previous/staged manifest、image state、summary、history 和备份验证状态继续使用同目录临时文件 + 原子 rename。受支持配置更新只能走 `update-deploy-env.sh <candidate> <expected-sha256>` 并共享严格锁；锁下竞争者返回 75，不会覆盖操作员值。dotenv 解析错误只输出 file/line/固定 category，不得输出 key、value 或 token。

GitHub secrets：

```text
OCI_REGISTRY=<harbor-registry>
OCI_NAMESPACE=iiot
OCI_REGISTRY_USERNAME=<harbor-robot-or-user>
OCI_REGISTRY_PASSWORD=<harbor-password-or-token>
DEPLOY_TARGET_DIR=/data/iiot-platform/cloud/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
SEED_ADMIN_PASSWORD=<固定 Cloud 管理员密码>
```

GitHub `cloud-image` / `cloud-deploy` 只作为灾备路径，且 workflow 已要求确认词。日常发布不得等待它们。

服务器端 `deploy-release.sh` 不再是日常或手工入口；日常由稳定 `runner/iiot-release-runner.sh` 接收请求。基础设施维护仍不得手工伪造旧 invocation/plan/support digest。本机 SSH 触发不可用时，恢复连接后优先用 `Deploy.ps1 -ResumeInvocation` 重发已构建请求。

发布脚本会：

1. 获取 Cloud release lock，并执行 `pre-deploy-check.sh`；它会同时校验 release/cleanup/config 锁、`.env`、镜像、`certs/` 和 `releases/*` 的 non-root 可访问性。release/cleanup 活锁 fail-fast，已证明 stale 的锁才可清理；config 锁永不 stale 自动清理。
2. 执行 `postgres-backup.sh`。
3. 根据 release tag 重写应用镜像坐标；全量发布重写五个应用镜像，按需发布必须已有 `current-release.env`，并基于当前 release 保留未选服务镜像。
4. `docker compose pull` 从 Harbor 拉取应用镜像。
5. 保持基础设施容器可用。
6. 运行 `iiot-migration`。
7. 启动应用容器；当本次发布影响浏览器流量时，启动或重启 `nginx-gateway`。
8. 执行 `post-deploy-check.sh` 和运维检查。
9. 在运行态健康通过后晋升 `current-release.env` 并写入基础 summary/history 状态。
10. 实时执行发布后磁盘清理：清 BuildKit cache、分开清理 Docker/containerd 旧应用镜像、删 Harbor 旧应用 tag 后执行或确认 GC，并把完整结果追加到 current/history summary。

若第 9 步已完成而 cleanup 失败，脚本会明确报告“runtime rollout healthy / cleanup failed”并返回非零；这属于运行版本已上线但发布收口失败。重试前必须先查 current release、容器镜像、release/cleanup 锁和 summary，不得盲目整轮重发同一 SHA。

`pre-deploy-check.sh` 会复用 `ops-check.sh` 检查运行状态、Outbox、队列和 Timescale 状态，但发布前不把旧备份状态作为强 gate；发布流程下一步会立即执行 `postgres-backup.sh`，备份失败会在任何容器更新前中止。pre-deploy 检查的是更新前的当前运行版本，默认使用 `REQUIRE_DATAWORKER_HEALTHCHECK=0`，避免旧 DataWorker 镜像缺少新 healthcheck 时阻断升级到修复版本。它也会在 OIDC 证书检查之前先验证 `releases/*` 状态文件是否仍对标准 non-root 部署用户可读可写，防止 root 应急路径留下的 owner/mode 漂移到真正发布阶段才爆炸。干净首部署没有 `current-release.env` 时不存在旧运行态可检查，preflight 摘要必须打印 `runtime-check-skipped-no-current-release`，不能写成 healthz/ops-check 已通过；已有当前版本时才打印 `healthz-http-local ops-check-runtime`。日常手工巡检直接执行 `./scripts/ops-check.sh` 时仍默认要求最新备份文件、checksum、新鲜度和 DataWorker Docker healthcheck 有效。

成功条件：

- `GET /internal/healthz` 在服务器本机返回 `200`。
- `GET /downloads` 返回 `200`。
- `GET /api/v1/public/client-downloads/latest?channel=stable&targetRuntime=win-x64` 返回 `200`。
- `GET /.well-known/openid-configuration` 返回 HTTP issuer。
- Cloud 左侧“打开助手”按钮不置灰，点击后进入 AICopilot Cloud OIDC challenge。
- `./scripts/post-deploy-check.sh` 返回 `0`。
- 需要把 OIDC token 发行纳入生产验收时，必须使用真实授权码流程生成的一次性 code 和 PKCE verifier，写入 0600 临时文件，并设置 `POST_DEPLOY_VERIFY_OIDC_TOKEN=1`、`POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE`、`POST_DEPLOY_OIDC_CODE_VERIFIER_FILE` 后运行 `./scripts/post-deploy-check.sh`；脚本会拒绝 group/other 有权限的 code/verifier 文件。不得伪造密码流、client_credentials 或把 discovery/JWKS 冒充 token 成功，也不得把 code/verifier 写入日志、文档、shell history、进程环境或 curl 进程参数。
- 关闭容器非 root 验收或验收 Edge 安装/更新发布时，必须额外运行 `POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=1 ./scripts/post-deploy-check.sh`，确认 public catalog、installer artifact、installer stub、Velopack `RELEASES`、channel manifests 和 `.nupkg` 静态下载都通过。
- `./scripts/ops-check.sh` 返回 `0`。
- `deploy/releases/current-release.env` 指向当前 release。
- `deploy/releases/current-release.summary.md` 包含本次部署的服务列表和 git 更新摘要。
- 发布总结包含清理前后磁盘摘要，且 `/data` 未超过部署阈值。
- 工作区 `latest-summary.md` 的 mode 必须是 `deploy` 且所有 step 成功；dry-run 或 validate-only 摘要不能作为真实发布证据。

干净首部署时 `latest-successful-verify.txt` 可能尚不存在，`ops-check.sh` 会打印 warning 但默认不阻断部署；每周恢复验证 cron 正常跑过后该字段会更新。若要把恢复验证缺失或过期作为强制失败，手工执行时设置 `REQUIRE_BACKUP_VERIFY=1 ./scripts/ops-check.sh`。

部署磁盘阈值固定：

- `/data` 达到 80% 时必须告警并输出占用摘要。
- `/data` 达到 85% 时必须先完成清理再继续普通部署。
- `/data` 达到 90% 时阻断非应急部署。
- 禁止执行 `docker system prune -a --volumes`。

发布后清理是主线；仍必须配置周级兜底清理 cron，避免部署半途中断导致 build cache 和旧镜像长期堆积。

## 定时备份

本目录只提供 cron 模板，不自动修改服务器 crontab：

- `deploy/cron/iiot-backup.cron.example`
- `deploy/cron/iiot-backup-verify.cron.example`
- `deploy/cron/iiot-post-release-cleanup.cron.example`

默认节奏：

- daily backup at `02:30`
- weekly restore verification at `03:30` every Sunday
- weekly post-release cleanup at `04:30` every Sunday

## EdgeClient 自动更新包

EdgeClient Velopack 更新包由现有 `nginx-gateway` 直接提供：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/stable/
```

服务器包目录由 `EDGE_UPDATES_DIR` 决定，当前生产服务器值为：

```text
/data/iiot-platform/edge-client/edge-updates/velopack/stable
```

目录中放：

- `RELEASES`
- `*.nupkg`
- 可选 `releases.*.json`

Cloud 生产配置中的 `EdgeInstallerArtifacts__VelopackReleasesBaseUrl` 填 channel 父目录：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack
```

客户端 `launcher.update.json` 的 `Source` 固定填生产 `stable` 渠道：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/stable/
```

`RELEASES` 和 `releases.*.json` 使用 `Cache-Control: no-cache`，`*.nupkg` 使用长期缓存。
