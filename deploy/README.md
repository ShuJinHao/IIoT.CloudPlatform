# IIoT Cloud Harbor CICD And Private Server Deploy

本目录是 `IIoT.CloudPlatform` 当前生产部署入口。云端部署以 Harbor 镜像仓库和私有服务器本地脚本为准；GitHub 只保留代码记录和可选 CI，不作为私有服务器直接部署入口。

## 部署口径

- 当前目标是 single-machine production starter。
- 生产版本统一使用 `release_tag = sha-*`，`latest` 不能作为生产应用版本。
- `cloud-image` 负责构建并推送五个应用镜像到 Harbor。
- 私有服务器通过本地上传 `deploy/` 和真实 `.env`，再在服务器执行部署脚本。
- 本轮允许清理旧测试部署和旧数据，不做旧 `docker stack` 到新 `docker compose` 的数据迁移。
- `deploy/scripts/deploy-release.sh` 是标准发布入口，`deploy/scripts/rollback-release.sh` 是应用镜像回滚入口。
- 运维、备份、恢复和检查细节见 [OPERATIONS.md](./OPERATIONS.md)。

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
- `/edge-updates/*`：EdgeClient 自动更新包下载。

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

CI 或本机构建必须把以下五个应用镜像推送到 Harbor，同一批版本使用同一个 `sha-*` tag：

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

GitHub workflow `cloud-image` 可用于构建推送 Harbor，只要配置上述 OCI secrets。`cloud-deploy` 只适用于 CI 能 SSH 到服务器的环境；当前私有服务器默认不走它。

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

部署前从本地上传：

```text
deploy/
deploy/.env
```

真实 `.env` 由运维管理，不提交仓库。`deploy/.env.example` 只作为模板。

首次部署或明确允许清空测试环境时，可以删除旧 stack、旧 compose 容器和旧卷，再按新版 compose 重新启动。例子：

```sh
docker stack ls
docker stack rm <old-stack-name>
docker compose -f /srv/iiot-cloud/deploy/docker-compose.prod.yml down -v
docker volume ls | grep -E 'iiot|postgres|rabbitmq|seq'
```

只在确认旧数据不需要保留时执行清空动作。

## .env 要点

应用镜像坐标必须指向 Harbor 仓库，tag 可以先写任意 `sha-*`；发布脚本会按传入 release tag 重写五个应用镜像 tag：

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

OIDC 签名证书是 Cloud 本地 token 签名证书，不是公网 HTTPS 证书，不需要购买。首次部署前在服务器生成一次并持久化：

```sh
sudo mkdir -p /srv/iiot-cloud/deploy/certs
openssl req -x509 -newkey rsa:2048 \
  -keyout /tmp/cloud-oidc-signing.key \
  -out /tmp/cloud-oidc-signing.crt \
  -days 3650 -nodes \
  -subj "/CN=IIoT Cloud OIDC"
openssl pkcs12 -export \
  -out /srv/iiot-cloud/deploy/certs/cloud-oidc-signing.pfx \
  -inkey /tmp/cloud-oidc-signing.key \
  -in /tmp/cloud-oidc-signing.crt \
  -passout pass:
rm -f /tmp/cloud-oidc-signing.key /tmp/cloud-oidc-signing.crt
sudo chmod 600 /srv/iiot-cloud/deploy/certs/cloud-oidc-signing.pfx
```

## 标准发布步骤

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
<PUBLIC_BASE_URL>/edge-updates/
```

服务器包目录默认：

```text
/srv/iiot/edge-updates
```

可通过 `EDGE_UPDATES_DIR` 覆盖。目录中放：

- `RELEASES`
- `*.nupkg`
- 可选 `releases.*.json`

客户端 `launcher.update.json` 的 `Source` 填：

```text
<PUBLIC_BASE_URL>/edge-updates/
```

`RELEASES` 和 `releases.*.json` 使用 `Cache-Control: no-cache`，`*.nupkg` 使用长期缓存。
