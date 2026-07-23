# AI只读接口契约

本文档约束 `IIoT.CloudPlatform` 暴露给 AICopilot 或其他 AI 调用方的只读业务数据接口。当前唯一真实业务数据源是 Cloud；Cloud 写入始终禁止，受控 Text-to-SQL 只可作为同一 Cloud 来源的只读兜底。

## 1. 基线

- 已沉淀业务域优先走 Cloud `GET /api/v1/ai/read/*` 插件接口；只有插件结构化返回同源 `Unsupported` 或 `Unavailable` 时，才允许进入同一 Cloud profile 的受控 Text-to-SQL。
- Cloud 不给 AI 暴露写接口；`AiReadController` 不得出现 `HttpPost`、`HttpPut`、`HttpPatch` 或 `HttpDelete`。
- AI 读取必须使用 `HttpApiPolicies.RequireAiReadToken`，并受 `HttpApiRateLimitPolicies.AiRead` 限流。
- 每个 `GetAiRead*Query` 必须实现 `IAiReadQuery<>`，并使用 `AuthorizeAiRead(AiReadPermissions.*)` 声明权限点。
- AI Read 不使用 Human 权限属性、Human Controller 或 Edge 设备身份写入口绕路。
- AI 返回值只允许业务需要的字段、范围摘要和结构化数据，不返回 SQL、prompt、原始请求参数、内部异常栈或生产表 raw payload。

### 1.1 Canonical owner

- `IIoT.CloudPlatform` 是八个 `/api/v1/ai/read/*` provider 端点、权限点、过滤语义、响应 DTO、限流与审计口径的 canonical owner。
- `AICopilot` 是 typed client、consumer 映射和独立 live-test 的 owner；不得在 consumer 端重新定义 Cloud 字段、权限、状态或过滤语义。
- 契约改动固定按 Cloud provider 测试 → AICopilot consumer 测试 → 双仓候选源码原字节 digest → 非生产真实 Gateway 联合验收的顺序闭合，并绑定双方 clean HEAD。

## 2. 允许域

当前 AI Read 允许读取以下 Cloud 只读域：

- 设备：`GET /api/v1/ai/read/devices`
- 工序：`GET /api/v1/ai/read/processes`
- 客户端发布版本：`GET /api/v1/ai/read/client-releases`
- 设备客户端状态：`GET /api/v1/ai/read/device-client-states`
- 产能汇总：`GET /api/v1/ai/read/capacity/summary`
- 小时产能：`GET /api/v1/ai/read/capacity/hourly`
- 设备日志：`GET /api/v1/ai/read/device-logs`
- 通用生产数据：`GET /api/v1/ai/read/production-records`

新增允许域前必须先补本文档、权限点、行为测试和 `AiReadHttpContractTests`。

## 3. 设备与工序主数据精确查询

`GET /api/v1/ai/read/devices` 只返回正式设备主数据字段：

- `id`
- `deviceCode`
- `deviceName`
- `processId`

查询支持 `deviceId`、`deviceCode`、`processId` 精确过滤，`keyword` 只模糊匹配设备编码和设备名称，`maxRows` 受统一上限约束。多个条件同时出现时必须按 AND 相交，并与 delegated device scope 在同一数据库查询中完成过滤、计数、稳定排序和分页；不得先拉全量设备到内存过滤。`deviceId` 越 delegated scope 返回 Forbidden；授权范围内不存在的 `deviceId`、未命中或只命中范围外设备的 `deviceCode` 返回空集，不能泄露范围外设备是否存在。`deviceCode` 只用于正式设备编码精确匹配，不得冒充 `DeviceId`。

`GET /api/v1/ai/read/processes` 只返回 `id`、`processCode`、`processName`，支持 `processId` 精确过滤、`keyword` 编码/名称模糊过滤和统一 `maxRows`。`processId` 与 `keyword` 同时出现时按 AND 相交；不存在时返回空集。

`Guid.Empty` 的 `deviceId` / `processId` 返回 400。`/devices` 当前不提供 `status`、`lineName`、`processName`、`updatedAt` 过滤；传入这些已知误导参数必须返回 400，不能静默忽略后返回未过滤集合。设备运行状态必须读取独立的 `device-client-states` 域，工序名称必须用 `/processes?processId=` 解析，不得把 GUID 或其它状态语义塞入 `keyword`。

### 3.1 设备客户端状态

`GET /api/v1/ai/read/device-client-states` 只接受 `AiRead.DeviceClientState` 权限，`AiRead.Device` 不能替代。它支持 `deviceId/deviceCode/processId/keyword/maxRows` AND 查询，授权范围、count、稳定排序和分页复用设备主数据数据库查询。返回集以当前授权 `Device` 为主集；无 `DeviceClientState` 的设备仍返回一行，`softwareStatus=MissingRuntimeHeartbeat`，所有投影专属字段和 `updatedAtUtc` 为 `null`。

`runtimeStatus` 保留最新运行心跳原值；`softwareStatus` 只由 `lastRuntimeHeartbeatAtUtc` 和同一响应捕获的 `asOfUtc` 解析。版本上报和投影更新时间不得刷新运行新鲜度。AI DTO 不返回 Human `issue`。当前不支持 `softwareStatus`、`runtimeStatus`、`status`、`lineName`、`processName`、`updatedAt`、`updatedAtUtc` 过滤；传入时必须返回 400。

## 4. 生产数据唯一入口

AI 读取生产记录只能使用：

```text
GET /api/v1/ai/read/production-records
```

禁止恢复或新增 AI 专用 `pass-stations/{typeKey}` 语义入口。Human 过站查询和 Edge 过站上传不属于 AI Read 表面，不能被 AICopilot 作为生产数据读取捷径。

`production-records` 支持的查询条件：

- `typeKey`：可选，生产数据类型。
- `processId`：可选，工序范围。
- `deviceId`：可选，正式设备 ID。
- `barcode`：可选，条码筛选；返回 scope 中只能标记 `present`，不得回显敏感原值。
- `result`：可选，结果筛选。
- `startTime` / `endTime` 或 `preset`：时间窗；二者不得混用。
- `fieldMode`：只允许 `list` 或 `full`。
- `maxRows`：必须被 `AiReadOptions.MaxRows` 截断。

跨类型查询必须提供 `deviceId` 或 `processId`。指定 `deviceId` 时必须通过 `AiReadQueryGuard.ValidateDeviceAllowed` 校验 delegated device scope。

## 5. 字段和数据过滤

- 生产数据返回公共列和 schema 化字段，不返回 raw `payload_jsonb`。
- `fieldMode=list` 只能返回 `pass-station-types.json` 的列表字段，并排除公共列。
- `fieldMode=full` 可以返回该工序 schema 定义字段，但仍不得绕过 schema 直接暴露 raw payload。
- 返回 scope 可以包含 `typeKey`、`processId`、`deviceId`、`fieldMode`、`preset`、`startTime`、`endTime`、`delegatedUserId` 和 delegated device 数量；不得回显 barcode 原文、SQL、prompt 或内部参数对象。
- 日志和文本字段必须按 `AiReadOptions` 截断，避免一次响应携带过长诊断文本。

## 6. 审计和验证

- QueryScope 只允许记录 GUID、时间、数字、布尔值和已经过严格闭集校验的规范化枚举。`keyword`、`barcode`、`deviceCode`、`plcName`、`channel`、`targetRuntime`、自由 `status`、`result`、动态 `typeKey` 等请求字符串只能记录固定 `present`，不得保存原值；包含分号、等号的输入也不能注入 scope 结构。
- 返回给调用方的 `queryScope` 与 `AiRead.Query` 审计摘要必须使用同一脱敏口径。prompt、token、Authorization、日志 message、请求 body、SQL 和其它自由文本不得进入范围摘要。
- AiRead 失败审计的 `FailureReason` 只记录稳定 `ResultStatus` 或异常类型，不保存 `Result.Errors`、`Exception.Message`、stack trace 或其它原始错误文本。
- 行为测试必须覆盖权限点、行数限制、时间窗、delegated device scope、生产数据字段过滤和旧 pass-station AI 入口禁用。
- `AiReadHttpContractTests` 必须守卫 `AiReadController` 的 GET-only 表面、`production-records` 唯一生产数据入口、`AuthorizeAiRead` 权限声明和 raw `payload_jsonb` 禁止暴露。
- AICopilot 查询前必须确认 Cloud 来源、数据域、设备或业务对象、时间范围和过滤条件；`Empty`、`NeedClarification`、`Unauthorized` 或凭据失败不得触发 Text-to-SQL。Text-to-SQL 必须经过唯一共享 AST guard、所选能力 profile 的表列 allowlist、只读数据库账号、限行/限时和审计，禁止跨源 fallback、隐藏 adapter 或后台写入。
- 新增可复用业务域时优先扩展 Cloud AI Read 插件契约；MES/ERP 等未来来源通过统一 provider/profile registry 注册，不复制 Runner、Guard、RepairLoop 或 Prompt。

## 7. 显式跨仓 live 验收

- 本节只在用户当前轮明确授权 `CrossProject` 或三端对齐时执行；普通业务开发、push、部署和 nightly 不得自动运行。显式执行时必须用当前候选源码启动隔离的真实 Cloud Aspire 环境，通过真实 Gateway HTTP 完成插件端点联合验收；StubHandler、手写 JSON 和 Simulation 不得冒充 provider。
- Cloud E2E 负责准备非生产数据和临时 AI service token，并只通过子进程环境把 BaseUrl/token 传给独立 `AICopilot.CloudAiReadLiveTests`；token 不得出现在命令参数、日志、summary、复盘或仓库。
- live 矩阵至少锁定八端点非空/空集/`maxRows=1` 截断、严格信封和字段集合、专用状态权限、Missing/Stale、自由文本 scope/审计脱敏以及稳定错误映射；显式运行缺环境变量必须失败，不能 Skip 后宣称通过。
- 联合验收记录必须包含两仓完整 baseline SHA、两仓候选源码 digest、UTC 时间和脱敏环境标识。任一仓库生产源码变化后旧记录立即失效，必须重跑。
