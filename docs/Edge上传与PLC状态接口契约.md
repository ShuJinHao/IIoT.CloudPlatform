# Edge 上传与 PLC 状态接口契约

本文档约束 Cloud 侧 Edge 上传身份链、PLC runtime state 写入边界和 Human 只读查询边界。

统一口径：Cloud 上的“上位机”不是手工创建出来的配置对象，而是已注册设备身份、Edge 客户端运行状态、客户端本地 PLC 配置和状态上报的只读呈现。Cloud 不新增、编辑、删除、启用或禁用现场 PLC。

## 1. 写入口

- PLC runtime state 只能由 Edge 设备身份写入：`POST /api/v1/edge/edge-hosts/plc-runtime-states`。
- Controller 必须使用 `RequireEdgeDeviceToken` 策略和 `EdgeHostPlcStateUpload` 限流策略。
- 写命令必须是 `IDeviceCommand`，不得挂到 Human、AI Read、Public 或 Internal surface。
- 请求中的 `DeviceId` 和 `ClientCode` 必须与 Cloud 设备身份链一致；`ClientCode` 只允许寻址和校验，不替代 `DeviceId`。
- 写入前只校验 `DeviceId + ClientCode` 是否属于已注册设备身份；不得要求 Cloud 侧存在 `EdgeHost` 配置记录，不得创建 PLC 绑定或设备主数据。
- 同一次上报不得包含重复 PLC 编码；PLC 编码和 ClientCode 必须按领域模型规范化。若 EdgeClient 仍以本地 PLC 设备名作为 `PlcCode`，Cloud 只能作为展示编码保存，不得把它解释成稳定资产编码。

## 2. 状态投影

- `edge_host_plc_runtime_states` 是 PLC runtime state 官方投影表，按 `DeviceId + ClientCode + PlcCode` 唯一。
- runtime state 不是聚合根，不得通过通用仓储写入，只能通过 `IEdgeHostPlcRuntimeStateStore` 维护。
- runtime state 不得依赖 `EdgeHostId` 或 `PlcBindingId`；唯一事实源是 Edge 客户端上报的 PLC 配置快照和运行状态。
- 未上报 PLC 时 Human 页面只能展示“客户端尚未上报 PLC 清单”，不得伪造在线、离线、启用或禁用状态。
- 设备删除时必须显式清理该设备的 `edge_host_plc_runtime_states`，并在影响查询和审计摘要中体现删除数量。

## 3. Human 只读

- Human 读取 PLC runtime state 只能走 `GET /api/v1/human/edge-hosts/{deviceId}/plc-runtime-states`。
- Human 查询必须使用 `EdgeHost.Read` 权限。
- Human 上位机列表和详情必须以当前人员可访问的 `Device` 为主数据源，左连 `DeviceClientState` 和 `EdgeHostPlcRuntimeState`，不得以旧 `EdgeHost` 配置表作为列表基准。
- Human 上位机列表的 count、分页和 keyword 过滤必须在数据库侧完成；只允许为当前页设备批量读取 `DeviceClientState` 和 `EdgeHostPlcRuntimeState`，不得为了搜索 PLC 字段把全部授权设备和全部 PLC 状态拉入内存后再分页。
- Human 查询只能展示设备身份、客户端运行状态和 runtime state 投影，不得反向修配置，不得写 `edge_host_plc_runtime_states`。
- Human API 和前端不得暴露新增、编辑、删除、启用、禁用上位机或 PLC 的入口；`EdgeHost.Manage` 权限点不得恢复。
- EdgeClient 应明确上报 `RuntimeStatus`；Cloud 仅做缺省兜底：未传 `RuntimeStatus` 且 `IsConnected=false`、`LastError` 非空时按 `Faulted` 展示，避免把有错误的连接失败误归为普通断开。

## 4. AI/Public 边界

- AI Read 只能读取被授权的生产数据，不得写 PLC runtime state。
- Public surface 不得暴露 PLC runtime state 写入口或查询入口。
- 任何新增 Agent、MCP、后台任务或适配器都不得绕过 Edge 设备身份链写 Cloud PLC runtime state。
- Text-to-SQL、RAG schema 或 AI 工具描述不得继续把 Cloud 描述成能维护“上位机 PLC 绑定”的系统。

## 5. 验收命令

```bash
rg -n "plc-runtime-states|ReportEdgeHostPlcRuntimeStates|IEdgeHostPlcRuntimeStateStore" IIoT.CloudPlatform/src IIoT.CloudPlatform/docs
dotnet test IIoT.CloudPlatform/src/tests/IIoT.ProductionService.Tests/IIoT.ProductionService.Tests.csproj --filter FullyQualifiedName~EdgeHostBehaviorTests --no-restore --disable-build-servers
dotnet test IIoT.CloudPlatform/src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --filter FullyQualifiedName~ConfigurationGuardTests --no-restore --disable-build-servers
```
