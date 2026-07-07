# Edge 上传与 PLC 状态接口契约

本文档约束 Cloud 侧 Edge 上传身份链、PLC runtime state 写入边界和 Human 只读查询边界。

## 1. 写入口

- PLC runtime state 只能由 Edge 设备身份写入：`POST /api/v1/edge/edge-hosts/plc-runtime-states`。
- Controller 必须使用 `RequireEdgeDeviceToken` 策略和 `EdgeHostPlcStateUpload` 限流策略。
- 写命令必须是 `IDeviceCommand`，不得挂到 Human、AI Read、Public 或 Internal surface。
- 请求中的 `DeviceId` 和 `ClientCode` 必须与 Cloud 设备身份链一致；`ClientCode` 只允许寻址和校验，不替代 `DeviceId`。
- 写入前必须找到对应 `EdgeHost`，未配置上位机时不得创建 `EdgeHost`、PLC 绑定或设备主数据。

## 2. 状态投影

- `edge_host_plc_runtime_states` 是 PLC runtime state 官方投影表，按 `DeviceId + ClientCode + PlcCode` 唯一。
- runtime state 不是聚合根，不得通过通用仓储写入，只能通过 `IEdgeHostPlcRuntimeStateStore` 维护。
- 已配置 PLC 上报状态时可以关联 `EdgeHostPlcBindingId`。
- 未配置但现场上报到的 PLC 允许进入 runtime state 投影，并标识为未配置；Cloud 不得自动创建、启用或修改 PLC 绑定配置。
- 同一次上报不得包含重复 PLC 编码；PLC 编码和 ClientCode 必须按领域模型规范化。

## 3. Human 只读

- Human 读取 PLC runtime state 只能走 `GET /api/v1/human/edge-hosts/{id}/plc-runtime-states`。
- Human 查询必须使用 `EdgeHost.Read` 权限。
- 查询只能合并展示配置元数据和 runtime state 投影，不得反向修配置，不得写 `edge_host_plc_runtime_states`。
- PLC 产能汇总只能走 Human 只读查询，必须同时具备 `EdgeHost.Read` 和 `Device.Read`，并继续执行人员设备授权范围过滤。
- 无业务设备、绑定禁用或无设备授权时，不得读取该 PLC 产能。

## 4. AI/Public 边界

- AI Read 只能读取被授权的生产数据，不得写 PLC runtime state。
- Public surface 不得暴露 PLC runtime state 写入口或查询入口。
- 任何新增 Agent、MCP、后台任务或适配器都不得绕过 Edge 设备身份链写 Cloud PLC runtime state。

## 5. 验收命令

```bash
rg -n "plc-runtime-states|ReportEdgeHostPlcRuntimeStates|IEdgeHostPlcRuntimeStateStore" IIoT.CloudPlatform/src IIoT.CloudPlatform/docs
dotnet test IIoT.CloudPlatform/src/tests/IIoT.ProductionService.Tests/IIoT.ProductionService.Tests.csproj --filter FullyQualifiedName~EdgeHostBehaviorTests --no-restore --disable-build-servers
dotnet test IIoT.CloudPlatform/src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj --filter FullyQualifiedName~ConfigurationGuardTests --no-restore --disable-build-servers
```
