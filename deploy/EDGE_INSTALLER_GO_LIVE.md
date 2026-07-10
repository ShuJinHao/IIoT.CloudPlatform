# Edge 客户端安装包上线清单

本文档用于把“Cloud 下载中心生成真实 `.exe` 安装包”上线到私有服务器。执行顺序不能倒：先部署 Cloud，再上传 Edge 安装素材，最后在 Windows 真机验收。上传阶段必须校验 `installer-artifact.json` 并生成正式 Cloud release 记录；catalog、首装和公开下载的版本集合只来自这些记录，文件系统不能补出未登记版本。三项目上传部署统一入口见 [上传部署总览](../../docs/上传部署总览.md)。

> 当前状态（2026-07-10）：本文描述的是目标上线清单；本轮没有执行真实 Cloud `stable` 上传/catalog/DB/静态下载闭环，也没有执行 Windows 安装/更新实机验收，不得据此宣称 Edge 安装更新已完成生产验收。

## 1. 本地前置检查

在 `IIoT.EdgeClient` 生成正式安装素材并校验：

```powershell
pwsh ./scripts/PublishEdgeClientInstallerArtifact.ps1 `
  -Version 1.2.0 `
  -ReleaseChannel stable `
  -CleanOutput

pwsh ./scripts/TestEdgeClientInstallerArtifact.ps1 `
  -ArtifactRoot publish/edge-installer-artifacts/stable/1.2.0 `
  -ExpectedVersion 1.2.0
```

成功后应存在：

```text
publish/edge-installer-artifacts/stable/1.2.0/
  IIoT.Edge.Setup.exe
  launcher/
  host/
  plugins/
  installer-artifact.json
```

Cloud 本地至少验证：

```bash
dotnet test src/tests/IIoT.ServiceLayer.Tests -p:BuildInParallel=false --disable-build-servers
dotnet build src/hosts/IIoT.AppHost -p:BuildInParallel=false --disable-build-servers
dotnet test src/tests/IIoT.EndToEndTests -p:BuildInParallel=false --disable-build-servers --filter ConfigurationGuardTests
cd src/ui/iiot-web && npm run build
```

完整 EndToEnd 依赖 Aspire 基础设施，若 RabbitMQ/eventbus 无法启动，不得把它当成安装包链路通过；至少保留上面的服务层、配置和前端构建证据。

## 2. 先部署 Cloud

必须先把当前 Cloud 后端和前端部署到服务器，否则线上没有 `installer-package` 接口，也没有新的 `.exe` 下载按钮。

标准方式由工作区唯一入口调度本机构建、Harbor 和 SSH 部署：

1. 推送或合并到 `main`。
2. 在本机确认 HEAD 已推送且工作区干净。
3. 从工作区根运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target Cloud -Services web`；如后端接口也改动，则按实际服务传入 `httpapi,gateway,dataworker,migration,web` 的子集。顶层入口从当前生产 profile 读取标准 Harbor、SSH 和 challenge 参数，再调度 Cloud 项目实现脚本。
4. 确认服务器 `post-deploy-check.sh`、`ops-check.sh` 和发布后清理摘要通过。

本机 `build-and-push.sh` 会给 Web Dockerfile 传入 `VITE_AICOPILOT_CHALLENGE_URL`，并使用 Harbor mirror 中的 `node:22-slim`、`nginx:1.27-alpine` 基础镜像。服务器不依赖 Docker Hub。单镜像 build/push 超过 15 分钟必须停止诊断，不得等待灾备 GitHub workflow。

本机 SSH 触发器不可用时，不得改走服务器手工 `deploy-release.sh`。该脚本已强制要求根入口 invocation/plan、run-bound image manifest、OCI digest 与预获取事务锁；必须恢复操作端/SSH 后从 `Invoke-WorkspaceDeploy.ps1` 重新预检或恢复。

部署前确认 `.env` 里保持密钥模式：

```text
EDGE_UPDATES_DIR=/data/iiot-platform/edge-client/edge-updates
```

部署后在服务器本机确认：

```sh
cd /data/iiot-platform/cloud/deploy
./scripts/post-deploy-check.sh
docker compose ps
```

## 3. 发布 Edge 素材

Edge 安装素材不在 CloudPlatform 仓库生成。当前有两个有效发布入口：

- 正式 GitHub 打包：`IIoT.EdgeClient` 的 `edge-pack-modules.yml` 只在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 上完整构建和发布，渠道固定为 `stable`。
- 日常宿主快发：操作者从工作区根运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgeHost -ReleaseNotesPath <更新说明.md>`；顶层入口调度 EdgeClient 本机脚本完成编译、Velopack、installer artifact 和 Cloud Human API 上传，渠道固定为 `stable`。这是本机运维快发路径，不是 GitHub CI/CD job；生产 stable 不允许 `rsync/scp`。
- 日常插件快发：只改工序插件时运行 `pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgePlugin -ModuleId <真实ModuleId> -ReleaseNotesPath <更新说明.md>`，只上传独立插件 zip 并登记插件 release，不生成宿主版本。
- 生产服务器只允许 `stable` 渠道，不保留 `ci`、`dev`、`test` 或其他测试渠道目录。

正式 GitHub 打包入口：

```text
workflow_dispatch
  version = 1.2.0
```

该 workflow 的 `package-runtime` job 必须跑在 GitHub hosted `windows-latest`，生成 `edge-installer-artifact` 和 `edge-velopack-releases`。随后 `publish-edge-updates` job 必须跑在内网 `[self-hosted, iiot-linux-prod]` runner，把 artifacts 本地发布到 `${EDGE_UPDATES_DIR}`。当前生产服务器 `EDGE_UPDATES_DIR=/data/iiot-platform/edge-client/edge-updates`。

日常快发入口示例：

```powershell
pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 `
  -Target EdgeHost `
  -ReleaseNotesPath <本次更新说明.md>
```

快发脚本未传 `-Version` 时会通过 Cloud Human catalog 查询 stable 最新版本并自动递增 patch。上传完成后，Cloud 服务端先校验和落盘 bundle，再从 manifest 派生 DB release 行、写入审计，按 SemVer 执行最新 3 个 stable 版本保留策略，并返回本次部署总结。脚本必须打印 version、sourceCommit、releaseNotes、上传耗时、限速、清理结果和 HTTP 验证结果。

发布后服务器必须有：

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
```

服务器检查：

```sh
find /data/iiot-platform/edge-client/edge-updates/installers/stable/1.2.0 -maxdepth 3 -printf '%y %P %s\n' | sort
```

外部只读检查 catalog 和素材 URL：

```sh
cd deploy
BASE_URL=http://<cloud-host>:81 \
CHANNEL=stable \
TARGET_RUNTIME=win-x64 \
EXPECTED_VERSION=1.2.0 \
./scripts/verify-edge-installer-catalog.sh
```

该脚本不会调用 `installer-package`，也不会轮换设备启动密钥。它会读取 `installer-artifact.json` 中的 `installerStubFile`，对实际安装器 URL 执行 HEAD 和 GET 校验；GET 必须返回 HTTP 200，且下载字节数大于 0 并匹配 manifest 中的安装器大小。

若下载中心提示“安装素材不存在”，优先检查 `iiot-httpapi` 的挂载和配置：

```text
${EDGE_UPDATES_DIR}:/app/edge-updates:rw
EdgeInstallerArtifacts__RootPath=/app/edge-updates/installers
EdgeInstallerArtifacts__VelopackReleasesBaseUrl=${PUBLIC_BASE_URL}/edge-updates/velopack
```

生产服务器不要只验证 nginx 静态路径。nginx 能返回 `installer-artifact.json` 只代表浏览器能下载静态文件；首装生成由 `iiot-httpapi` 读取同一份素材，必须同时验证容器内可见：

```sh
docker inspect deploy-iiot-httpapi-1 \
  --format '{{range .Mounts}}{{println .Source "->" .Destination .Mode}}{{end}}'

docker exec deploy-iiot-httpapi-1 /bin/sh -lc \
  'find /app/edge-updates/installers/stable/1.2.0 -maxdepth 3 -printf "%y %P %s\n" | sort'

curl -sS -I http://127.0.0.1:81/edge-updates/installers/stable/1.2.0/installer-artifact.json
```

如果需要看访问日志，优先用 `docker logs deploy-nginx-gateway-1`。`/var/log/nginx/access.log` 在 nginx 容器内可能指向 stdout 设备，直接 `grep` 文件会卡住等待流。

## 4. 验收前建设备

首装下载必须选择设备唯一码。验收前在 Cloud 设备管理里创建测试设备，且设备名称不能与已有设备重名。

生成安装包会轮换所选设备的 bootstrap secret。下载后应立刻用最新 `.exe` 验收，不要继续使用旧下载包或旧密钥。

## 5. Windows 真机验收

在 Windows 机器执行：

1. 登录 `http://<cloud-host>:81`。
2. 进入“客户端下载中心 -> 首装下载”。
3. 勾选插件，选择测试设备唯一码。
4. 点击“下载安装包”。
5. 确认下载文件是 `.exe`，不是 `iiot-binding.json`。
6. 先在本机或 Windows 上校验下载包结构，确认它不是空安装器外壳：

   ```powershell
   pwsh <IIoT.EdgeClient>\scripts\TestEdgeDownloadedInstallerPackage.ps1 `
     -InstallerPath <下载到的 IIoT.Edge.Setup*.exe> `
     -ExpectedModuleId <真实ModuleId>
   ```

   该脚本只读 `.exe`，不执行安装，不打印 bootstrap secret。

7. 双击安装包，确认 Launcher 启动。
8. 确认只显示所选工序。
9. 确认 Cloud bootstrap 成功，设备身份链仍是 `ClientCode -> bootstrap -> DeviceId`。

也可以在 Windows 上用脚本执行 6-8 步的本机验收：

```powershell
pwsh <IIoT.EdgeClient>\scripts\InvokeEdgeInstallerWindowsAcceptance.ps1 `
  -InstallerPath <下载到的 IIoT.Edge.Setup*.exe> `
  -ExpectedModuleId <真实ModuleId>
```

如果需要清理旧安装目录后做干净验收，必须显式确认：

```powershell
pwsh <IIoT.EdgeClient>\scripts\InvokeEdgeInstallerWindowsAcceptance.ps1 `
  -InstallerPath <下载到的 IIoT.Edge.Setup*.exe> `
  -ExpectedModuleId <真实ModuleId> `
  -CleanInstallRoot `
  -ConfirmCleanInstallRoot
```

该脚本会运行安装器并检查 `%LOCALAPPDATA%\IIoTEdge`、Launcher、`host/`、`plugins/<ModuleId>/`、绑定导入摘要和机器配置；最后的 Cloud bootstrap 成功仍需看 Launcher/Cloud 运行状态确认。

若需要绕过页面做 API 级下载验证，可在确认测试设备后运行：

```powershell
pwsh .\deploy\scripts\InvokeEdgeInstallerPackageDownload.ps1 `
  -CloudApiBaseUrl http://<cloud-host>:81/api/v1 `
  -CloudToken <publish-or-admin-token> `
  -DeviceId <测试设备 id> `
  -ModuleId <真实ModuleId> `
  -Channel stable `
  -TargetRuntime win-x64 `
  -OutputDirectory .\downloads `
  -ConfirmSecretRotation
```

该命令同样会轮换所选设备的 bootstrap secret，只能对确认的测试/部署设备执行。
未显式传 `-BaseUrl` 时，脚本会从 `-CloudApiBaseUrl` 推导公开 Gateway origin 写入首装包。

失败处理：

- 下载仍是 JSON：线上 Web 或 API 没部署到新版本。
- 接口 404：线上 HttpApi 没有 `installer-package`。
- “安装素材不存在”：素材未上传到挂载目录，或 `EdgeInstallerArtifacts__RootPath` 不一致。
- “服务器内部错误”且日志含 `System.OutOfMemoryException`：线上仍是旧版 `installer-package`，还在内存里重打旧 `layout.zip` 后 `ToArray()`。必须部署目录组合版本，并重新上传目录型素材；Cloud 只读取宿主/插件目录，把绑定 JSON 注入本次下载 payload，不写回共享素材目录。
- bootstrap 失败：检查客户端是否携带 `X-IIoT-Bootstrap-Secret`、设备是否被重新生成安装包、Windows 使用的是否是最新 `.exe`。
