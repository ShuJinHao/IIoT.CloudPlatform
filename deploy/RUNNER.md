# GitHub Self-hosted Runner

本文只描述 runner 准备要求。安装 runner、创建用户、授权 Docker 和写入 GitHub 注册 token 是人工操作项，不由部署脚本自动执行。三项目上传部署统一入口见 [上传部署总览](../../docs/上传部署总览.md)。

## 目标

CloudPlatform 标准生产发布已经改为操作者本机构建、推 Harbor、SSH 触发服务器 `deploy-release.sh`。self-hosted runner 不再是日常发布链路，只作为灾备 GitHub workflow、CI 辅助和历史运维入口使用。

灾备 runner 链路为：

```text
git push GitHub
-> 操作者手动 workflow_dispatch 并输入灾备确认词
-> 10.98.90.154 内网 self-hosted runner 执行
-> cloud-image 构建并 push 到 10.98.90.154:80 Harbor
-> cloud-deploy 在 ${DEPLOY_TARGET_DIR} 执行 deploy-release.sh
```

runner 需要能出站访问 GitHub，用于接收 job 和下载 Actions；不需要把服务器 SSH 或 Docker 端口暴露到公网。

## Runner 要求

- 安装在 `10.98.90.154`，或能直接访问 `10.98.90.154:80` Harbor 和 `${DEPLOY_TARGET_DIR}` 的同网段 Linux 主机。
- GitHub runner label 必须包含 `iiot-linux-prod`。
- runner 进程必须使用专用非 root 用户运行，建议用户名 `github-runner`。
- `github-runner` 需要加入 `docker` 用户组。
- `github-runner` 需要能读写 `${DEPLOY_TARGET_DIR}`，但不要把 runner 工作目录放在生产数据目录里。
- 当前服务器 Docker Root Dir 固定为 `/data/iiot-platform/runtime/docker`，不要回退到系统盘 `/var/lib/docker`。
- 当前三仓 runner 工作目录固定为：
  - CloudPlatform：`/data/github-runner/cloud`
  - AICopilot：`/data/github-runner/aicopilot`
  - EdgeClient：`/data/github-runner/edgeclient`
- 服务器需要可访问 `github.com`、`api.github.com`、`objects.githubusercontent.com`、`github-releases.githubusercontent.com`、`pipelines.actions.githubusercontent.com`、`mcr.microsoft.com`、`api.nuget.org` 和 `registry.npmjs.org`。
- 服务器不要求访问 Docker Hub；Docker Hub 第三方镜像必须先同步到 Harbor mirror。
- 当前内网环境 Git smart HTTP 可能超时；workflow 已使用 GitHub archive/codeload 兜底拉取源码，不能改回只依赖 `actions/checkout`。

## 建议权限模型

```sh
sudo useradd --create-home --shell /bin/bash github-runner
sudo usermod -aG docker github-runner
sudo mkdir -p /data/github-runner/cloud /data/iiot-platform/cloud/deploy /data/iiot-platform/cloud/deploy/certs
sudo chown -R github-runner:github-runner /data/github-runner/cloud /data/iiot-platform/cloud/deploy
```

如果 `${DEPLOY_TARGET_DIR}` 已有生产数据，调整权限前先确认现有 owner 和 backup 策略，不要删除 `.env`、`backups/`、`certs/`、`releases/`。当前生产服务器 `DEPLOY_TARGET_DIR=/data/iiot-platform/cloud/deploy`。
`certs/` 必须允许 `github-runner` 写入；首次部署时 `pre-deploy-check.sh` 会生成持久化的 Cloud OIDC PFX。

## GitHub Secrets

CloudPlatform 仓库需要：

```text
OCI_REGISTRY=10.98.90.154:80
OCI_NAMESPACE=iiot
OCI_REGISTRY_USERNAME=<Harbor robot 或用户>
OCI_REGISTRY_PASSWORD=<Harbor 密码或 token>
DEPLOY_TARGET_DIR=/data/iiot-platform/cloud/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
SEED_ADMIN_PASSWORD=<固定 Cloud 管理员密码>
```

`DEPLOY_ENV_FILE` 是完整 `.env` 文本，不是文件路径。`OCI_REGISTRY_USERNAME` / `OCI_REGISTRY_PASSWORD` 对应的 Harbor 用户或 robot 需要 push、pull 和删除应用镜像 tag 权限。Cloud 管理员密码不放入 `DEPLOY_ENV_FILE`；灾备 `cloud-deploy` 和 `cloud-admin-repair` 使用单独的 `SEED_ADMIN_PASSWORD` secret 写入服务器 `.env`。

## 验收

runner 装好后，在服务器上确认：

```sh
id -u
docker version
docker buildx version
docker compose version
curl -I https://github.com
curl -I http://127.0.0.1:80/v2/
```

`id -u` 不能返回 `0`。Harbor `/v2/` 返回 `401 Unauthorized` 属于正常，说明 Harbor 可达但需要登录。
