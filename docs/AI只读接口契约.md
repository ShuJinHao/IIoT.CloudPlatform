# AI只读接口契约

> **状态：第 0～7 批候选代码保留；复审发现的问题尚未全部收口，当前不是生产部署基线。真实 Windows 验收、生产绑定迁移和部署仍未执行。**

本文档约束 `IIoT.CloudPlatform` 暴露给 AICopilot 或其他 AI 调用方的只读业务数据接口。当前唯一真实业务数据源是 Cloud；Cloud 写入始终禁止。typed AiRead 保留，真实 Cloud Direct DB 与 Text-to-SQL 当前整体关闭。

## 1. 基线

- 已沉淀业务域只走 Cloud `GET /api/v1/ai/read/*` typed 接口。`Unsupported`、`Unavailable`、空结果或任何其它 typed 结果当前都不得转入真实 Cloud Direct DB/Text-to-SQL；不得把用户范围只放进 prompt 后放行。
- Cloud 不给 AI 暴露写接口；`AiReadController` 不得出现 `HttpPost`、`HttpPut`、`HttpPatch` 或 `HttpDelete`。
- 交互式 AI 读取必须使用 `HttpApiPolicies.RequireAiReadDelegation`，并受 `HttpApiRateLimitPolicies.AiRead` 限流。
- 每个 `GetAiRead*Query` 必须实现 `IAiReadQuery<>`，并使用 `AuthorizeAiRead(AiReadPermissions.*)` 声明权限点。
- AI Read 不使用 Human 权限属性、Human Controller 或 Edge 设备身份写入口绕路。
- AI 返回值只允许业务需要的字段、范围摘要和结构化数据，不返回 SQL、prompt、原始请求参数、内部异常栈或生产表 raw payload。

### 1.1 当前用户委托与系统身份分离

- 每个交互式 AiRead 请求必须绑定当前已认证 AI 用户对应的当前有效 Cloud 用户委托，并在 Cloud 侧复核委托主体、有效期、撤销状态、人员状态、权限和设备范围。
- 委托固定使用 scope `iiot.ai.read`、audience `iiot-cloud-ai-read`、actor `ai-delegated-user`，最长有效期 30 分钟且不签发 refresh token；`sub` 必须与唯一 `delegated_user_id` 完全相同。Token 不携带作为授权真值的角色、权限或设备列表。
- 委托缺失、过期、撤销、无效、主体不匹配或无法复核时直接拒绝；禁止把缺失声明解释为 `Global`，也禁止以“未给范围”等价于全舰队。
- 交互式 AiRead Token 只能由 Cloud OIDC 在运行时为当前用户签发，不从 Keychain、环境变量或配置读取静态 AiRead Token。外部登录 Cookie 只允许在授权码＋PKCE 登录完成前临时保存 Token，AICopilot 完成加密委托落库后不得把它返回浏览器、模型上下文、AgentSession、日志或审计正文。
- 身份状态系统 Token 使用独立 actor `ai-identity-status-system`、audience `iiot-cloud-identity-status`、签名密钥、认证策略和端点，只能访问身份状态接口；它访问任意 `/api/v1/ai/read/*` 必须失败。任何 service/system Token 都不得代替当前用户执行交互式读取，也不能由用户请求自动切换过去。
- delegated device scope 必须与当前 Cloud 授权求交后在同一数据库查询中执行；consumer 侧过滤、旧 token 声明或空 scope 均不能扩大结果。
- 每次请求都以 Cloud 当前账号启用状态、员工在职状态、`status_version`、当前角色、对应 `AiRead.*` 权限和设备授权为准。普通用户当前授权设备为零时返回空集合；只有实时确认仍为 Cloud `Admin` 时才可获得全设备范围。
- Cloud 账号、员工、角色、权限或设备授权变化不依赖 AICopilot 清理任务，必须在下一次 Cloud 请求中实时失效。OpenIddict 本地验证同时开启 token-entry validation，Cloud 中已撤销的委托 Token 不得等到 JWT 自然过期。
- AICopilot 当前登录 grant 的主动注销和过期元数据清理属于 consumer Identity 生命周期；它不取代上述 Cloud 每请求实时复核。
- 身份状态 system Token 由 AICopilot 使用专用签名密钥在内存中生成，issuer 固定 `iiot-cloud-system`、audience 固定 `iiot-cloud-identity-status`、actor 固定 `ai-identity-status-system`、subject 固定 `aicopilot-identity-status`。Cloud 要求唯一 `iat/nbf/exp`、`nbf=iat`、`0 < exp-iat <= 5 分钟` 且 `ClockSkew=0`；缺字段、重复字段、过长寿命或 delegated actor 全部拒绝。AICopilot 剩余 60 秒时续签；首次 `401` 只允许清缓存、续签并重试一次，再失败必须关闭。部署不再签发或注入固定长期 system Token。

### 1.2 Canonical owner

- `IIoT.CloudPlatform` 是全部 `/api/v1/ai/read/*` provider 端点、权限点、过滤语义、响应 DTO、限流与审计口径的 canonical owner；端点数量不得作为固定业务假设。
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
- 设备插件 PLC 元数据：`GET /api/v1/ai/read/device-plcs?deviceId=...`
- 设备插件数据能力：`GET /api/v1/ai/read/data-schemas?deviceId=...&plcCode=...`

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

### 3.2 设备插件、PLC 与数据 Schema 动态解析

AI 必须先通过 `/processes` 和 `/devices` 把人员输入解析到唯一正式 `processId`、`deviceId`，再读取以下动态元数据：

- `device-plcs` 只接受非空 `deviceId`，并使用 `AiRead.Device` 权限和 delegated device scope；返回设备、实际安装插件版本、PLC 编码/名称、权威性、配置版本、快照采集/接收时间、新鲜度、启用状态、协议/地址和真实运行状态。
- `data-schemas` 只接受非空 `deviceId`，可按稳定 `plcCode` 收窄，并使用 `AiRead.ProductionRecord` 权限；返回该设备绑定插件“实际安装版本”签名能力清单中的 `TypeKey`、Schema、作用范围、查询模式和公开字段。
- Human 与 AiRead 可以共用底层解析服务，但必须保留不同权限入口、范围过滤和审计。AI 不得调用 Human Controller，也不得读取未授权设备后在 consumer 侧过滤。
- PLC 新鲜度由 Cloud 唯一配置管理，默认窗口为 3 分钟；`age == 3 分钟` 仍有效，只有 `age > 3 分钟` 才过期。Human、AiRead、`device-plcs` 与 `data-schemas` 必须共用同一个 resolver 和同一个 `asOfUtc` 口径，不得各自复制阈值或比较符。
- 没有权威 PLC 快照、按上述规则判定快照过期、实际插件版本缺失或能力清单与实际版本不匹配时，必须返回明确的 `Unavailable`/过期事实，不能冒充空 PLC、固定 Schema 或当前在线。
- 设备、PLC 或 `TypeKey` 为零个或多个匹配时必须返回需要澄清；只有正式 `deviceId`、`plcCode` 和 `TypeKey` 唯一解析且范围已确认后，才允许执行生产数据查询。

## 4. 生产数据唯一入口

AI 读取生产记录只能使用：

```text
GET /api/v1/ai/read/production-records
```

禁止恢复或新增 AI 专用 `pass-stations/{typeKey}` 语义入口。Human 过站查询和 Edge 过站上传不属于 AI Read 表面，不能被 AICopilot 作为生产数据读取捷径。

`production-records` 支持的查询条件：

- `typeKey`：设备插件声明的业务记录类别。一个插件版本可以声明多种，调用方必须从该设备实际安装版本的 `data-schemas` 解析，不能从 `ProcessType`、`ModuleId` 或中文名称推导。
- `processId`：可选，工序范围。
- `deviceId`：可选，正式设备 ID。
- `plcCode`：可选，稳定 PLC 编码精确筛选。
- `plcName`：可选，中文 PLC 名称精确筛选。
- `barcode`：可选，条码筛选；返回 scope 中只能标记 `present`，不得回显敏感原值。
- `result`：可选，结果筛选。
- `startTime` / `endTime` 或 `preset`：时间窗；二者不得混用。
- `fieldMode`：只允许 `list` 或 `full`。
- `maxRows`：必须被 `AiReadOptions.MaxRows` 截断。

Cloud 是所有 typed GET 响应字段的 canonical provider owner，以下字段语义不得由 consumer 猜造或补值：

- `capacity/summary` 的 `okCount`、`ngCount` 为 nullable；Cloud 没有可靠质量事实时返回 `null`，不得补零。
- `capacity/hourly` 每行必须返回非空稳定 `plcCode`；`okCount`、`ngCount`、`okRate` 为 nullable，质量事实缺失时保持 `null`。
- `production-records` 的 CP/AP v2 记录通过 `fields.clipSlot` 返回 `MG1` 或 `MG2`，并在 `fieldSchema` 中声明该字段；历史缺字段记录只允许保持缺失，不得伪造槽位。

跨类型查询必须提供 `deviceId` 或 `processId`。指定 `deviceId` 时必须通过 `AiReadQueryGuard.ValidateDeviceAllowed` 校验 delegated device scope。

当前 AP、CP 插件只启用各自已确认的模切完成记录；它们的历史 `TypeKey` 是兼容数据值，不代表工序、模块或设备身份，也不能限制未来插件声明其它经业务确认的记录类别。模切完成 Schema 可以返回 `plcCode`、`plcName`、`clipSlot`、`startTime`、`punchingQuantity`、`punchingSpeed` 等已声明字段。`ClientCode` 始终是设备插件唯一跨端业务身份，普通 AI 回答默认隐藏；仅在设备消歧、授权范围说明或审计确有需要时，才可与人员可读设备名称一起展示。Cloud 内部 `DeviceId`、`P2-CPUC` / `P1-APUC` 等 MES 身份都不是普通 AI 查询或展示字段。

## 5. 字段和数据过滤

- 生产数据返回公共列和 schema 化字段，不返回 raw `payload_jsonb`。
- `fieldMode=list` 只能返回 `pass-station-types.json` 的列表字段，并排除公共列。
- `fieldMode=full` 可以返回该工序 schema 定义字段，但仍不得绕过 schema 直接暴露 raw payload。
- 返回 scope 可以包含 `typeKey`、`processId`、`deviceId`、`fieldMode`、`preset`、`startTime`、`endTime`、`delegatedUserId` 和 delegated device 数量；`plcCode`、`plcName` 与 barcode 等请求字符串只能标记 `present`，不得回显原文、SQL、prompt 或内部参数对象。
- 日志和文本字段必须按 `AiReadOptions` 截断，避免一次响应携带过长诊断文本。

## 6. 审计和验证

- QueryScope 只允许记录 GUID、时间、数字、布尔值和已经过严格闭集校验的规范化枚举。`keyword`、`barcode`、`deviceCode`、`plcName`、`channel`、`targetRuntime`、自由 `status`、`result`、动态 `typeKey` 等请求字符串只能记录固定 `present`，不得保存原值；包含分号、等号的输入也不能注入 scope 结构。
- 返回给调用方的 `queryScope` 与 `AiRead.Query` 审计摘要必须使用同一脱敏口径。prompt、token、Authorization、日志 message、请求 body、SQL 和其它自由文本不得进入范围摘要。
- AiRead 失败审计的 `FailureReason` 只记录稳定 `ResultStatus` 或异常类型，不保存 `Result.Errors`、`Exception.Message`、stack trace 或其它原始错误文本。
- 行为测试必须覆盖权限点、行数限制、时间窗、delegated device scope、生产数据字段过滤和旧 pass-station AI 入口禁用。
- `AiReadHttpContractTests` 必须守卫 `AiReadController` 的 GET-only 表面、`production-records` 唯一生产数据入口、`AuthorizeAiRead` 权限声明和 raw `payload_jsonb` 禁止暴露。
- AICopilot 查询前必须确认 Cloud 来源、数据域、设备或业务对象、时间范围和过滤条件，并携带当前有效 Cloud 用户委托；`Empty`、`NeedClarification`、`Unauthorized`、凭据失败和所有其它结果都不得触发真实 Cloud Direct DB/Text-to-SQL。
- 真实 Cloud Direct DB/Text-to-SQL 只有在委托范围贯穿 SQL 执行层，并由可验证 AST 强制行条件或数据库 RLS 独立证明范围后，才允许另行复审；只读账号、表列 allowlist、限行/限时、审计或 prompt 约束单独存在都不足以开放。
- 新增可复用业务域时优先扩展 Cloud AI Read 插件契约；MES/ERP 等未来来源通过统一 provider/profile registry 注册，不复制 Runner、Guard、RepairLoop 或 Prompt。

## 7. 显式跨仓 live 验收

- 本节只在用户当前轮明确授权 `CrossProject` 或三端对齐时执行；普通业务开发、push、部署和 nightly 不得自动运行。显式执行时必须用当前候选源码启动隔离的真实 Cloud Aspire 环境，通过真实 Gateway HTTP 完成插件端点联合验收；StubHandler、手写 JSON 和 Simulation 不得冒充 provider。
- Cloud E2E 负责准备非生产数据和当前测试用户的短期委托，并只通过子进程环境把 BaseUrl/token 传给独立 `AICopilot.CloudAiReadLiveTests`；不得用身份状态系统 Token 或静态 AiRead Token 替代。委托不得出现在命令参数、日志、summary、复盘或仓库。
- live 矩阵至少锁定全部活动端点的非空/空集/`maxRows=1` 截断、严格信封和字段集合、专用状态权限、动态 PLC/Schema 权限与范围、Missing/Stale/Unavailable、自由文本 scope/审计脱敏以及稳定错误映射；显式运行缺环境变量必须失败，不能 Skip 后宣称通过。
- 联合验收记录必须包含两仓完整 baseline SHA、两仓候选源码 digest、UTC 时间和脱敏环境标识。任一仓库生产源码变化后旧记录立即失效，必须重跑。
