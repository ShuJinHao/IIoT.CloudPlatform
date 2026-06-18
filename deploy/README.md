# IIoT Cloud Harbor CICD And Private Server Deploy

本目录是 `IIoT.CloudPlatform` 当前生产部署入口。云端部署以 Harbor 镜像仓库、内网 GitHub self-hosted runner 和服务器本地发布脚本为准；GitHub 托管 runner 不能访问 `10.98.90.154:80` Harbor，也不能 SSH 到内网服务器，不作为生产部署执行环境。

## 部署口径

- 当前目标是 single-machine production starter。
- 生产版本统一使用 `release_tag = sha-*`，`latest` 不能作为生产应用版本。
- `cloud-image` 在内网 self-hosted runner `iiot-linux-prod` 上按变更路径构建并推送受影响的应用镜像到 Harbor；共享代码、构建配置或手动触发会构建全部应用镜像。
- `cloud-deploy` 在同一内网 runner 上同步 `deploy/` 模板、写入生产 `.env`，再执行 `deploy/scripts/deploy-release.sh`；未传 `services` 时全量发布，传入 `services` 时只拉取并重启指定服务。
- runner 必须使用专用非 root 用户运行，例如 `github-runner`，不能用 root 跑 Actions 服务。
- 服务器 Docker Root Dir 固定为 `/data/docker`，runner 工作目录固定在 `/data/github-runner/*`，不要把构建缓存和 runner workdir 放回系统盘。
- Docker Hub 不作为生产依赖源；compose 第三方镜像和 Web Dockerfile 的 Node/Nginx 基础镜像必须先同步到 Harbor mirror。
- Edge 客户端安装素材不进 Harbor；日常 push main 只跑 smoke，完整 GitHub 打包只在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 时执行；日常快发可以由操作者本机运行 `IIoT.EdgeClient/scripts/LocalPublishAndDeploy.ps1` 发布到服务器 `${EDGE_UPDATES_DIR}/installers/{channel}/{version}` 和 `${EDGE_UPDATES_DIR}/velopack/{channel}`。
- Cloud catalog 会扫描 `/app/edge-updates/installers/{channel}/{version}/installer-artifact.json` 并与数据库 release 记录合并；同 key 数据库记录优先，可用于 Draft/Archived 抑制文件落盘版本。
- 本地手工 Docker 构建和服务器手工部署只作为应急 fallback，不是标准 Cloud/AI 发布流程。
- `deploy/scripts/deploy-release.sh` 是标准发布入口，`deploy/scripts/rollback-release.sh` 是应用镜像回滚入口。
- self-hosted runner 安装和权限要求见 [RUNNER.md](./RUNNER.md)。
- 运维、备份、恢复和检查细节见 [OPERATIONS.md](./OPERATIONS.md)。
- Cloud 下载中心生成 Edge 客户端 `.exe` 的上线顺序见 [EDGE_INSTALLER_GO_LIVE.md](./EDGE_INSTALLER_GO_LIVE.md)。

## 运行拓扑

入口链路固定：

```text
nginx-gateway -> iiot-gateway -> iiot-httpapi
iiot-dataworker -> RabbitMQ queues
iiot-migration -> one-shot migration/bootstrap job
```

外部暴露：

- `${GATEWAY_HTTP_PORT:-80}`：产品入口。
- `/api/v1/human/*`：人工端 API。
- `/api/v1/edge/*`：边端上传 API。
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

- `OCI_REGISTRY`：Harbor 地址，例如 `harbor.example.com`。
- `OCI_NAMESPACE`：Harbor 项目/命名空间，例如 `iiot`。
- `OCI_REGISTRY_USERNAME`：Harbor 登录用户名。
- `OCI_REGISTRY_PASSWORD`：Harbor 登录密码或 robot account token。

GitHub workflow `cloud-image` 和 `cloud-deploy` 是标准生产链路，但必须跑在带 `iiot-linux-prod` label 的内网 self-hosted runner 上。不要把这两个 workflow 改回 `ubuntu-latest`，公网 GitHub runner 访问不了内网 Harbor 和部署目录。

`cloud-image` 会按路径判断需要构建的镜像：只改 `src/hosts/IIoT.HttpApi/` 时只构建 `iiot-httpapi`，只改 `src/ui/iiot-web/` 时只构建 `iiot-web`；改 `src/core/`、`src/shared/`、`src/services/`、`src/infrastructure/`、`Directory.Build.props`、`global.json` 或手动触发时构建全部应用镜像。push 触发范围只覆盖宿主、共享代码、Web、compose 和 nginx 配置，文档类变更不会触发镜像构建。构建使用 Harbor registry cache，第二次构建会复用已有 Docker layer。

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

## Edge 安装素材

Edge 客户端产物不属于 Cloud Docker 镜像，也不进入 Harbor。当前流程分三类：

- `push main`：只跑 smoke 编译和测试，不发布安装包。
- `workflow_dispatch` 或 `edge-v*` / `v*` tag：由 GitHub hosted `windows-latest` 构建 runtime、installer artifact 和 Velopack releases，再由内网 `iiot-linux-prod` runner 把 GitHub Actions artifacts 发布到 `${EDGE_UPDATES_DIR}`。
- 日常快发：操作者本机运行 `IIoT.EdgeClient/scripts/LocalPublishAndDeploy.ps1`，本机编译和打包后通过 rsync/scp 发布到 `${EDGE_UPDATES_DIR}`。这是本机运维快发路径，不是 GitHub CI/CD job。

GitHub 完整打包流程固定为：

```text
IIoT.EdgeClient workflow_dispatch / tag
-> windows-latest 构建 edge-installer-artifact 和 edge-velopack-releases
-> 上传 GitHub Actions artifacts
-> iiot-linux-prod 内网 self-hosted runner 下载 artifacts
-> 本地发布到 ${EDGE_UPDATES_DIR:-/srv/iiot/edge-updates}
```

CloudPlatform 不负责构建或上传 Edge 安装素材，只负责把 `${EDGE_UPDATES_DIR}` 只读挂载给 HttpApi 和 Nginx。发布后的目录固定为：

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

`iiot-httpapi` 以只读方式挂载 `${EDGE_UPDATES_DIR}` 到 `/app/edge-updates`，通过 `EdgeInstallerArtifacts__RootPath=/app/edge-updates/installers` 读取安装素材，并通过 `EdgeInstallerArtifacts__VelopackReleasesBaseUrl=${PUBLIC_BASE_URL}/edge-updates/velopack` 返回运行时更新源。Cloud 的公开下载目录、Edge catalog 和 Human catalog 会扫描 `installer-artifact.json` v2 并与数据库 release 记录合并，因此本机快发文件落盘后可以被客户端看到；如果数据库已有同 channel/version/runtime 或 module/channel/version/runtime 记录，则数据库状态优先，Draft/Archived 可抑制同 key 文件版本。登录 Cloud 后，“客户端下载中心 -> 首装下载”会按 `installer-artifact.json` v2 选择一份 `host/` 和所选 `plugins/<ModuleId>/`，把 `launcher/iiot-binding.json`、`launcher/iiot-enabled-plugins.json`、`launcher/launcher.update.json`、`plugins/<ModuleId>/iiot-plugin-binding.json` 注入本次下载的安装器 payload，并返回真正的 `.exe`。这些绑定配置不写回素材目录、不落盘到共享模板、不写日志。

`iiot-web` 是 Vite 静态构建，Cloud 左侧“打开助手”按钮需要在构建镜像时注入 AICopilot challenge URL：

```text
VITE_AICOPILOT_CHALLENGE_URL=http://10.98.90.154:82/api/identity/cloud-oidc/challenge
```

未注入时按钮会按设计置灰。

## 服务器部署目录

推荐服务器目录：

```text
/srv/iiot-cloud/deploy
```

标准流程由 `cloud-deploy` workflow 自动同步：

```text
deploy/
deploy/.env
```

真实 `.env` 由 GitHub secret `DEPLOY_ENV_FILE` 注入，不提交仓库。`deploy/.env.example` 只作为模板。应急手工部署时，才需要人工把 `deploy/` 和真实 `.env` 放到 `/srv/iiot-cloud/deploy`。

生产服务器以容器标签为准确认真实部署目录，不要按旧路径猜：

```sh
docker inspect deploy-iiot-httpapi-1 \
  --format 'working={{index .Config.Labels "com.docker.compose.project.working_dir"}} config={{index .Config.Labels "com.docker.compose.project.config_files"}} project={{index .Config.Labels "com.docker.compose.project"}}'
```

2026-06-18 现场校准：`jms.hdc-group.cn` / `10.98.90.154` 的 Cloud compose 工作目录为 `/srv/iiot-cloud/deploy`，Docker Root Dir 为 `/data/docker`，Edge 更新素材目录为 `/srv/iiot/edge-updates -> /data/iiot/edge-updates`，Cloud runner 工作目录为 `/data/github-runner/cloud`。如果服务器目录和本文不一致，先用上面的标签命令对齐真实目录，再修改文档。

首次部署或明确允许清空测试环境时，可以删除旧 stack、旧 compose 容器和旧卷，再按新版 compose 重新启动。例子：

```sh
docker stack ls
docker stack rm <old-stack-name>
docker compose -f /srv/iiot-cloud/deploy/docker-compose.prod.yml down -v
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

这些值必须替换为生产真实值：

- `PUBLIC_BASE_URL`
- `PG_PASSWORD`
- `RABBITMQ_DEFAULT_PASS`
- `JWTSETTINGS__SECRET`
- `SEQ_ADMIN_PASSWORD`
- `SEED_ADMIN_PASSWORD`
- `SEQ_API_KEY`（启用 Seq ingestion key 时）

这些值通常保持模板默认：

- `GATEWAY_HTTP_PORT`
- `SEQ_HOST_PORT`
- `RABBITMQ_MANAGEMENT_PORT`
- `EDGE_UPDATES_DIR`
- `BOOTSTRAP_AUTH_REQUIRE_SECRET`
- `BACKUP_RETENTION_DAYS`
- `BACKUP_MAX_AGE_HOURS`
- `BACKUP_VERIFY_MAX_AGE_DAYS`

`PUBLIC_BASE_URL` 必须是 origin，不要以 `/` 结尾，例如：

```text
PUBLIC_BASE_URL=http://10.98.90.154:81
```

内网 HTTP OIDC 要显式打开，且 Cloud issuer 和 AICopilot callback 必须使用内网地址：

```text
ALLOW_INTRANET_HTTP_OIDC=true
OIDC_PROVIDER_ISSUER=http://10.98.90.154:81
AICOPILOT_OIDC_REDIRECT_URI=http://10.98.90.154:82/api/identity/cloud-oidc/callback
AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI=http://10.98.90.154:82/login
OIDC_PROVIDER_CERTS_DIR=/srv/iiot-cloud/deploy/certs
OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH=/app/certs/cloud-oidc-signing.pfx
OIDC_PROVIDER_SIGNING_CERTIFICATE_PASSWORD=
VITE_AICOPILOT_CHALLENGE_URL=http://10.98.90.154:82/api/identity/cloud-oidc/challenge
```

`AICOPILOT_OIDC_REDIRECT_URI` 改动后必须让 `iiot-migration` 重新跑一次，重新 seed OIDC client，否则 Cloud 仍会使用旧回调地址。

OIDC 签名证书是 Cloud 本地 token 签名证书，不是公网 HTTPS 证书，不需要购买。`pre-deploy-check.sh` 会调用 `ensure-oidc-signing-cert.sh`，在 `OIDC_PROVIDER_CERTS_DIR` 下没有 PFX 时自动生成持久化自签名 PFX，并设置为 `600`。前提是该目录存在且 `github-runner` 可写：

```sh
sudo mkdir -p /srv/iiot-cloud/deploy/certs
sudo chown -R github-runner:github-runner /srv/iiot-cloud/deploy/certs
sudo chmod 755 /srv/iiot-cloud/deploy/certs
```

## 标准发布步骤

标准路径：

1. 合并或推送到 `main`。
2. `cloud-ci` 默认只跑快速验证：restore/build、ServiceLayer、ConfigurationGuard、前端 build、compose config；完整 EndToEnd 只在手动 `workflow_dispatch` 勾选时运行。
3. `cloud-image` 在 `iiot-linux-prod` self-hosted runner 上构建五个应用镜像，并推送到 Harbor，tag 为 `sha-${GITHUB_SHA}`。
4. 人工触发 `cloud-deploy`，输入 `release_tag = sha-*`。
5. `cloud-deploy` 校验 runner 非 root、同步 `deploy/`、写入 `DEPLOY_ENV_FILE`、登录 Harbor，并执行 `deploy/scripts/deploy-release.sh`。

GitHub secrets：

```text
OCI_REGISTRY=10.98.90.154:80
OCI_NAMESPACE=iiot
OCI_REGISTRY_USERNAME=<harbor-robot-or-user>
OCI_REGISTRY_PASSWORD=<harbor-password-or-token>
DEPLOY_TARGET_DIR=/srv/iiot-cloud/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
```

应急手工路径只在 Actions 不可用时使用。

在服务器上执行：

```sh
cd /srv/iiot-cloud/deploy
chmod +x ./scripts/*.sh
docker login <OCI_REGISTRY> --username <OCI_REGISTRY_USERNAME>
DEPLOY_GIT_SHA=<git-sha> DEPLOY_TRIGGERED_BY=manual ./scripts/deploy-release.sh sha-0123456789abcdef
```

发布脚本会：

1. 执行 `pre-deploy-check.sh`。
2. 执行 `postgres-backup.sh`。
3. 根据 release tag 重写五个应用镜像坐标。
4. `docker compose pull` 从 Harbor 拉取应用镜像。
5. 保持基础设施容器可用。
6. 运行 `iiot-migration`。
7. 启动应用容器和 `nginx-gateway`。
8. 执行 `post-deploy-check.sh` 和运维检查。
9. 写入 `deploy/releases/current-release.env`、`previous-release.env`、`staged-release.env` 和 `history/`。

成功条件：

- `GET /internal/healthz` 在服务器本机返回 `200`。
- `GET /downloads` 返回 `200`。
- `GET /api/v1/public/client-downloads/latest?channel=stable&targetRuntime=win-x64` 返回 `200`。
- `GET /.well-known/openid-configuration` 返回 HTTP issuer。
- Cloud 左侧“打开助手”按钮不置灰，点击后进入 AICopilot Cloud OIDC challenge。
- `./scripts/post-deploy-check.sh` 返回 `0`。
- `./scripts/ops-check.sh` 返回 `0`。
- `deploy/releases/current-release.env` 指向当前 release。

干净首部署时 `latest-successful-verify.txt` 可能尚不存在，`ops-check.sh` 会打印 warning 但默认不阻断部署；每周恢复验证 cron 正常跑过后该字段会更新。若要把恢复验证缺失或过期作为强制失败，手工执行时设置 `REQUIRE_BACKUP_VERIFY=1 ./scripts/ops-check.sh`。

## 定时备份

本目录只提供 cron 模板，不自动修改服务器 crontab：

- `deploy/cron/iiot-backup.cron.example`
- `deploy/cron/iiot-backup-verify.cron.example`

默认节奏：

- daily backup at `02:30`
- weekly restore verification at `03:30` every Sunday

## EdgeClient 自动更新包

EdgeClient Velopack 更新包由现有 `nginx-gateway` 直接提供：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/{channel}/
```

服务器包目录默认：

```text
/srv/iiot/edge-updates/velopack/{channel}
```

可通过 `EDGE_UPDATES_DIR` 覆盖。目录中放：

- `RELEASES`
- `*.nupkg`
- 可选 `releases.*.json`

Cloud 生产配置中的 `EdgeInstallerArtifacts__VelopackReleasesBaseUrl` 填 channel 父目录：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack
```

客户端 `launcher.update.json` 的 `Source` 填：

```text
<PUBLIC_BASE_URL>/edge-updates/velopack/{channel}/
```

`RELEASES` 和 `releases.*.json` 使用 `Cache-Control: no-cache`，`*.nupkg` 使用长期缓存。
