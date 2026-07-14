# GitHub Self-hosted Runner

本文只描述 stable runner 准备和维护要求。Cloud 日常应用发布的操作员/AI 唯一入口是工作区 `deploy/Deploy-Changed.ps1 -Targets Cloud`；本文不授权直接触发 runner、workflow、SSH 或项目脚本。三项目上传部署统一入口见 [上传部署总览](../../docs/上传部署总览.md)。

## 目标

CloudPlatform 日常发布由 `Deploy-Changed.ps1` 选定受影响服务，再由工作区 `Deploy.ps1` 作为内部执行器生成不可伪造的 invocation/request 契约，最终由已安装的 stable runner 以 `github-runner` 非 root 身份消费。日常应用发布不得安装、升级、同步或覆盖 runner 文件；Runner 安装/升级只能在独立维护窗口由工作区 `Deploy.ps1 -InstallRunner` 执行，并在后续重新运行 `-Doctor`。`Invoke-WorkspaceDeploy.ps1`、直接 Harbor/SSH 命令和旧 `cloud-image` / `cloud-deploy` 只保留为内部实现、基础设施维护或明确灾备/恢复，不是日常替代入口。

runner 需要能出站访问 GitHub，用于接收 job 和下载 Actions；不需要把服务器 SSH 或 Docker 端口暴露到公网。

## Runner 要求

- 安装在 Cloud/Harbor 同网段 Linux 主机，且能直接访问内网 Harbor 和 `${DEPLOY_TARGET_DIR}`。
- GitHub runner label 必须包含 `iiot-linux-prod`。
- runner 进程必须使用专用非 root 用户运行，建议用户名 `github-runner`。
- `github-runner` 需要加入 `docker` 用户组。
- `github-runner` 需要能读写 `${DEPLOY_TARGET_DIR}`，但不要把 runner 工作目录放在生产数据目录里。
- 当前服务器 Docker Root Dir 固定为 `/data/iiot-platform/runtime/docker`，不要回退到系统盘 `/var/lib/docker`。
- 当前三仓 runner 工作目录固定为：
  - CloudPlatform：`/data/github-runner/cloud`
  - AICopilot：`/data/iiot-platform/runners/aicopilot`
  - EdgeClient：`/data/github-runner/edgeclient`
- 服务器需要可访问 `github.com`、`api.github.com`、`objects.githubusercontent.com`、`github-releases.githubusercontent.com`、`pipelines.actions.githubusercontent.com`、`mcr.microsoft.com`、`api.nuget.org` 和 `registry.npmjs.org`。
- 服务器不要求访问 Docker Hub；Docker Hub 第三方镜像必须先同步到 Harbor mirror。
- 当前内网环境 Git smart HTTP 可能超时；workflow 已使用 GitHub archive/codeload 兜底拉取源码，不能改回只依赖 `actions/checkout`。
- stable runner 安装完成后必须视为受控基础设施：日常 request 只能读取/执行 pinned 副本，任何“如果存在就覆盖”或随应用发布同步 runner 的实现都是阻断项。

## 建议权限模型

```sh
sudo useradd --create-home --shell /bin/bash github-runner
sudo usermod -aG docker github-runner
sudo mkdir -p /data/github-runner/cloud /data/iiot-platform/cloud/deploy /data/iiot-platform/cloud/deploy/certs
sudo chown -R github-runner:github-runner /data/github-runner/cloud /data/iiot-platform/cloud/deploy
```

如果 `${DEPLOY_TARGET_DIR}` 已有生产数据，调整权限前先确认现有 owner 和 backup 策略，不要删除 `.env`、`backups/`、`certs/`、`releases/`。当前生产服务器 `DEPLOY_TARGET_DIR=/data/iiot-platform/cloud/deploy`。
`certs/` 必须允许 `github-runner` 写入；首次部署时 `pre-deploy-check.sh` 会生成持久化的 Cloud OIDC PFX。
`releases/` 和 `releases/history/` 也必须允许 `github-runner` 读写；已有 `current-release.env`、`previous-release.env`、`staged-release.env` 和 `current-release.summary.md` 一旦被 root 应急路径写成 root-owned，下一轮标准 non-root 发布会被 preflight 直接拦下。

## GitHub Secrets

CloudPlatform 仓库需要：

```text
OCI_REGISTRY=<harbor-registry>
OCI_NAMESPACE=iiot
OCI_REGISTRY_USERNAME=<Harbor robot 或用户>
OCI_REGISTRY_PASSWORD=<Harbor 密码或 token>
DEPLOY_TARGET_DIR=/data/iiot-platform/cloud/deploy
DEPLOY_ENV_FILE=<完整生产 .env 内容>
SEED_ADMIN_PASSWORD=<固定 Cloud 管理员密码>
```

`DEPLOY_ENV_FILE` 是完整 `.env` 文本，不是文件路径。`OCI_REGISTRY_USERNAME` / `OCI_REGISTRY_PASSWORD` 对应的 Harbor 用户或 robot 需要 push、pull 和删除应用镜像 tag 权限。Cloud 管理员密码不放入 `DEPLOY_ENV_FILE`；灾备 `cloud-deploy` 和 `cloud-admin-repair` 使用单独的 `SEED_ADMIN_PASSWORD` secret 写入服务器 `.env`。

长期方向是取消长期生产 secret 常驻 self-hosted runner，改为 GitHub OIDC + Vault 或等价短期凭据签发。短期内必须至少使用 GitHub production environment protection、专用非 root runner 用户、最小 Harbor 权限、`DEPLOY_ENV_FILE` 0600 写入和 workflow 灾备确认词；Runner 只是 `Deploy-Changed` 内部执行组件，不得被当成可直接触发的第二个日常生产发布入口。

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
