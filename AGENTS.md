# IIoT.CloudPlatform Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责项目路由和少量 Cloud 硬边界。

## 按需路由

- 进入 Cloud 实际修改后，只读取 `docs/云端规则.md` 中与本批模块直接相关的章节、相关源码和受影响测试。
- 只有过站工序任务才读 `docs/过站工序扩展规则.md`；其它专题契约同样只在直接触碰对应边界时读取。
- 只有修改 `src/ui/iiot-web` 时才读取该目录的 `AGENTS.md`。
- 部署或生产配置只读取工作区部署总览、`docs/云端规则.md` 部署章节和当前目标部署文档的相关章节。
- 项目复盘、历史记录、旧计划和证据只在回归、冻结链路冲突、失败原因不明、同类故障追溯或用户明确要求时按关键词读取命中邻域。

## 项目硬边界

- Cloud 是人员、权限、设备、配方、设备寻址和生产归档查询的业务源头；新设备注册仅限管理员。
- `ClientCode` 只用于 bootstrap/寻址且不得维护改写，`DeviceId` 才是正式归档身份；设备删除必须显式检查依赖、二次确认并审计。
- 配方修改必须创建新版本；新普通过站工序默认只扩展配置，不重开身份、上传或部署主链。
- AICopilot 面向 Cloud 的能力保持只读，不得新增隐藏写接口或绕过正式 AiRead/只读数据源边界。

## 任务与部署

- 沟通/审计只读且不运行测试；业务开发只运行 Architecture、Security 和 owner 选出的受影响 Business。全量、coverage、mutation、duplication、Quality、CrossProject 和三端对齐只在用户明确授权时运行；影响无法归属时停止。
- 普通部署只走工作区 `deploy/Deploy-Changed.ps1`：要求 clean、已提交的 `main`，可 push 现有 HEAD，不创建提交、不编辑文件，只补同 SHA 受影响 Architecture/Security/DeploymentContract 并发布受影响服务。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1`；Cloud 阶段处理 release history、基础设施、migration、seed 和健康，不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 只有形成长期规则、修复历史回归、处理生产事故或改变部署机制时，才更新项目复盘。
