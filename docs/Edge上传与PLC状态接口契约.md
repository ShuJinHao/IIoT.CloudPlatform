# Edge 上传与 PLC 状态接口契约

> **状态：第 0～7 批候选代码保留；复审发现的问题尚未全部收口，当前不是生产部署基线。真实 Windows 验收、生产绑定迁移和部署仍未执行。**

本文档约束 Cloud 侧 Edge 上传身份链、PLC 当前快照写入边界，以及 Human/AiRead 只读查询边界。业务归属继承 `BR-CLOUD-022`、`BR-DATA-015`～`BR-DATA-020`：PLC 配置由设备插件独立数据库维护，当前运行状态来自插件采集内存，Cloud 只保存 Edge 上报的权威投影，不直接访问插件数据库。

统一口径：Cloud 上的“上位机”不是手工创建出来的配置对象，而是已注册设备身份、Edge 客户端运行状态、客户端本地 PLC 配置和状态上报的只读呈现。Cloud 不新增、编辑、删除、启用或禁用现场 PLC。

## 1. 写入口

- PLC runtime state 只能由 Edge 设备身份写入：`POST /api/v1/edge/edge-hosts/plc-runtime-states`。
- 安装 Binding v3 对 PLC 状态只允许字段 `paths.edgeHostPlcRuntimeStates`，导入后唯一映射为 `CloudApi:Paths:EdgeHostPlcRuntimeStates`，值固定为 `/api/v1/edge/edge-hosts/plc-runtime-states`。`paths.plcSnapshot`、`CloudApi:Paths:PlcSnapshot` 及少 `/edge` 段的 `/api/v1/edge-hosts/plc-runtime-states` 都不是新 Binding 的合法别名。
- 安装 Binding v3 对过站批量上传只允许字段 `paths.passStationBatchTemplate`，导入后唯一映射为 `CloudApi:Paths:PassStationBatchTemplate`，值固定为 `/api/v1/edge/pass-stations/{typeKey}/batch`；模板必须保留且仅替换 `{typeKey}`。`paths.passStationBatch` 和 `CloudApi:Paths:PassStationBatch` 不得形成第二套字段。
- Controller 必须使用 `RequireEdgeDeviceToken` 策略和 `EdgeHostPlcStateUpload` 限流策略。
- 写命令必须是 `IDeviceCommand`，不得挂到 Human、AI Read、Public 或 Internal surface。
- 请求中的 `DeviceId` 和 `ClientCode` 必须指向同一个 Cloud 设备插件；`ClientCode` 是跨端业务身份，`DeviceId` 只作为 Cloud 内部主键和授权关联值。
- 写入前只校验 `DeviceId + ClientCode` 是否属于已注册设备身份；不得要求 Cloud 侧存在 `EdgeHost` 配置记录，不得创建 PLC 绑定或设备主数据。
- 同一次上报不得包含重复 PLC 编码；PLC 编码和 ClientCode 必须按领域模型规范化。
- `PlcCode` 是 EdgeClient 持久化且改名不变的 PLC 稳定身份，`ReportedPlcName` 才是允许随本地设备改名变化的展示名称。Cloud 必须按 `PlcCode` 续写同一投影，不得用展示名称重新识别 PLC。
- Edge 只能提交完整的权威 PLC 快照。非权威、缓存不可用、缓存损坏或数据库回源失败时必须跳过本轮上报，不得把异常转换成空数组。
- 非空快照用于新增或替换本次出现的 PLC，并在同一保存单元中删除本次权威快照未出现的旧编码；整个写入必须原子完成。
- 空数组默认没有清空语义。只有 `IsAuthoritative=true`、`ClearPlcList=true`、`ConfigurationVersion` 非空且代表新配置清单时，才是“权威零 PLC”并允许清空投影。旧客户端空数组、缺少任一标记或显式非权威请求都必须拒绝并保留上一投影。
- `ReportedAtUtc`、`ConfigurationVersion`、权威标记、清空意图和快照内容共同参与幂等与并发围栏；旧时间或同版本冲突内容不得覆盖较新事实。

## 2. 状态投影

- `edge_host_plc_runtime_states` 是 PLC 当前状态投影表，按 `DeviceId + ClientCode + PlcCode` 唯一；它不是生产历史数据库。
- runtime state 不是聚合根，不得通过通用仓储写入，只能通过 `IEdgeHostPlcRuntimeStateStore` 维护。
- runtime state 不得依赖 `EdgeHostId` 或 `PlcBindingId`；唯一事实源是 Edge 客户端上报的 PLC 配置快照和运行状态。
- 完整快照必须先完成身份、字段和重复编码校验；请求无效时不得部分新增、替换或删除投影。
- PLC 快照新鲜度窗口由 Cloud 统一配置，默认为 3 分钟。每次读取以同一请求捕获的 `utcNow` 与投影接收时间计算 `age`；恰好 3 分钟仍有效，只有 `age > 3 分钟`（或大于当前 Cloud 配置值）才过期。没有权威快照时返回 `Unavailable`；过期时可以保留最后一次投影作为历史证据，但不得继续显示当前在线，也不得把不可用冒充为零 PLC。
- `Admin` 级联删除设备插件时必须在同一原子事务中清理该设备的 `edge_host_plc_runtime_states`，并在结构化审计中记录实际删除数量；PLC 投影或其它依赖不得阻止删除，影响查询不是强制前置条件。

## 3. Human 只读

- Human 读取 PLC runtime state 只能走 `GET /api/v1/human/edge-hosts/{deviceId}/plc-runtime-states`。
- Human 按设备读取动态 PLC 清单使用 `GET /api/v1/human/edge-hosts/{deviceId}/plcs`；读取该设备实际安装插件版本声明的数据能力使用 `GET /api/v1/human/edge-hosts/{deviceId}/data-schemas?plcCode=...`。
- Human 查询必须使用 `EdgeHost.Read` 权限。
- Human 统一“设备运行与版本”主视图使用 `DeviceClientOverview.Read`，主列表不得返回或预取 PLC runtime state；只有详情抽屉具备 `EdgeHost.Read` 时才允许调用上述 PLC 专属接口。
- Human 上位机列表和详情必须以当前人员可访问的 `Device` 为主数据源，左连 `DeviceClientState` 和 `EdgeHostPlcRuntimeState`，不得以旧 `EdgeHost` 配置表作为列表基准。
- Human 上位机 PLC 总览必须在每次 handler 中捕获一次 `utcNow`，并与 AiRead、`device-plcs`和 `data-schemas` 动态元数据共用同一 Cloud 配置和同一 PLC freshness resolver；不得复制阈值或在各 handler 中自行实现判定。快照 `age` 恰好等于窗口时仍有效，只有 `age > window` 才过期；过期后即使最后上报的 `RuntimeStatus=Connected`，也不得显示在线，必须返回过期/不可用语义并展示最后采集或接收时间。
- Human 上位机列表的 count、分页和 keyword 过滤必须在数据库侧完成；只允许为当前页设备批量读取 `DeviceClientState` 和 `EdgeHostPlcRuntimeState`，不得为了搜索 PLC 字段把全部授权设备和全部 PLC 状态拉入内存后再分页。
- Human 查询只能展示设备身份、客户端运行状态和 runtime state 投影，不得反向修配置，不得写 `edge_host_plc_runtime_states`。
- Human API 和前端不得暴露新增、编辑、删除、启用、禁用上位机或 PLC 的入口；`EdgeHost.Manage` 权限点不得恢复。
- EdgeClient 必须明确上报 `RuntimeStatus`：已连接为 `Connected`；未连接且明确故障或存在 `LastError` 为 `Faulted`；尚无快照、未知或无错误连接中为 `Unknown`；其余已确认未连接状态为 `Disconnected`。Cloud 仅做旧客户端缺省兜底：未传 `RuntimeStatus` 且 `IsConnected=false`、`LastError` 非空时按 `Faulted` 展示。

## 4. AI/Public 边界

- AI Read 只能读取被授权的生产数据，不得写 PLC runtime state。动态元数据只允许通过 `GET /api/v1/ai/read/device-plcs?deviceId=...` 和 `GET /api/v1/ai/read/data-schemas?deviceId=...&plcCode=...` 读取。
- Human 与 AiRead 共用底层解析服务，但必须经过各自独立的 Controller、权限点、设备范围过滤和审计入口；任何一方都不得借用另一方权限。
- PLC 清单响应必须包含设备、实际插件版本、PLC 编码/名称、权威性、配置版本、快照时间、新鲜度和当前状态。Schema 响应只来自该设备绑定插件的实际安装版本及其签名 `data-capabilities.json`，不得从 `ModuleId`、`ProcessType` 或中文名称猜测 `TypeKey`。
- 实际插件版本缺失、能力清单与已安装版本不匹配、快照不可用或状态过期时必须明确返回不可用/过期事实；不得返回固定 AP/CP、P1/P2、12 个 PLC 或模拟 Schema。
- Public surface 不得暴露 PLC runtime state 写入口或查询入口。
- 任何新增 Agent、MCP、后台任务或适配器都不得绕过 Edge 设备身份链写 Cloud PLC runtime state。
- Text-to-SQL、RAG schema 或 AI 工具描述不得继续把 Cloud 描述成能维护“上位机 PLC 绑定”的系统。

## 5. 验收命令

```bash
rg -n "plc-runtime-states|ReportEdgeHostPlcRuntimeStates|IEdgeHostPlcRuntimeStateStore" IIoT.CloudPlatform/src IIoT.CloudPlatform/docs
dotnet test src/tests/IIoT.CloudPlatform.ApplicationTests/IIoT.CloudPlatform.ApplicationTests.csproj -c Release --no-build --no-restore --disable-build-servers
dotnet test src/tests/IIoT.CloudPlatform.ArchitectureTests/IIoT.CloudPlatform.ArchitectureTests.csproj -c Release --no-build --no-restore --disable-build-servers
```
