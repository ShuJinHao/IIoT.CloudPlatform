# GitHub Self-hosted Runner

本文只描述 runner 准备要求。安装 runner、创建用户、授权 Docker 和写入 GitHub 注册 token 是人工操作项，不由部署脚本自动执行。

## 目标

标准部署链路固定为：

```text
git push GitHub
-> GitHub Actions 触发
-> 10.98.90.154 内网 self-hosted runner 执行
-> cloud-image 构建并 push 到 10.98.90.154:80 Harbor
-> cloud-deploy 在 /srv/iiot-cloud/deploy 执行 deploy-release.sh
```

runner 需要能出站访问 GitHub，用于接收 job 和下载 Actions；不需要把服务器 SSH 或 Docker 端口暴露到公网。

## Runner 要求

- 安装在 `10.98.90.154`，或能直接访问 `10.98.90.154:80` Harbor 和 `/srv/iiot-cloud/deploy` 的同网段 Linux 主机。
- GitHub runner label 必须包含 `iiot-linux-prod`。
- runner 进程必须使用专用非 root 用户运行，建议用户名 `github-runner`。
- `github-runner` 需要加入 `docker` 用户组。
- `github-runner` 需要能读写 `/srv/iiot-cloud/deploy`，但不要把 runner 工作目录放在生产数据目录里。
- 服务器需要可访问 `github.com`、`api.github.com`、`objects.githubusercontent.com`、`github-releases.githubusercontent.com`、`pipelines.actions.githubusercontent.com`、`mcr.microsoft.com`、`api.nuget.org` 和 `registry.npmjs.org`。
- 服务器不要求访问 Docker Hub；Docker Hub 第三方镜像必须先同步到 Harbor mirror。

## 建议权限模型

```sh
sudo useradd --create-home --shell /bin/bash github-runner
sudo usermod -aG docker github-runner
sudo mkdir -p /srv/github-runner /srv/iiot-cloud/deploy
sudo chown -R github-runner:github-runner /srv/github-runner /srv/iiot-cloud/deploy
```

如果 `/srv/iiot-cloud/deploy` 已有生产数据，调整权限前先确认现有 owner 和 backup 策略，不要删除 `.env`、`backups/`、`certs/`、`releases/`。

## GitHub Secrets

CloudPlatform 仓库需要：

```text
OCI_REGISTRY=10.98.90.154:80
OCI_NAMESPACE=iiot
OCI_REGISTRY_USERNAME=<Harbor robot 或用户>
OCI_REGISTRY_PASSWORD=<Harbor 密码或 token>
DEPLOY_TARGET_DIR=/srv/iiot-cloud/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
```

`DEPLOY_ENV_FILE` 是完整 `.env` 文本，不是文件路径。

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
