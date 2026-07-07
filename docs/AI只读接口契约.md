# AI只读接口契约

本文档约束 `IIoT.CloudPlatform` 暴露给 AICopilot 或其他 AI 调用方的只读业务数据接口。当前契约只覆盖 Cloud 项目，不授权 AICopilot 直连生产库或写 Cloud。

## 1. 基线

- AI 业务读取只能走 Cloud `GET /api/v1/ai/read/*`。
- Cloud 不给 AI 暴露写接口；`AiReadController` 不得出现 `HttpPost`、`HttpPut`、`HttpPatch` 或 `HttpDelete`。
- AI 读取必须使用 `HttpApiPolicies.RequireAiReadToken`，并受 `HttpApiRateLimitPolicies.AiRead` 限流。
- 每个 `GetAiRead*Query` 必须实现 `IAiReadQuery<>`，并使用 `AuthorizeAiRead(AiReadPermissions.*)` 声明权限点。
- AI Read 不使用 Human 权限属性、Human Controller 或 Edge 设备身份写入口绕路。
- AI 返回值只允许业务需要的字段、范围摘要和结构化数据，不返回 SQL、prompt、原始请求参数、内部异常栈或生产表 raw payload。

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

新增允许域前必须先补本文档、权限点、行为测试和 ConfigurationGuardTests。

## 3. 生产数据唯一入口

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

## 4. 字段和数据过滤

- 生产数据返回公共列和 schema 化字段，不返回 raw `payload_jsonb`。
- `fieldMode=list` 只能返回 `pass-station-types.json` 的列表字段，并排除公共列。
- `fieldMode=full` 可以返回该工序 schema 定义字段，但仍不得绕过 schema 直接暴露 raw payload。
- 返回 scope 可以包含 `typeKey`、`processId`、`deviceId`、`fieldMode`、`preset`、`startTime`、`endTime`、`delegatedUserId` 和 delegated device 数量；不得回显 barcode 原文、SQL、prompt 或内部参数对象。
- 日志和文本字段必须按 `AiReadOptions` 截断，避免一次响应携带过长诊断文本。

## 5. 审计和验证

- 行为测试必须覆盖权限点、行数限制、时间窗、delegated device scope、生产数据字段过滤和旧 pass-station AI 入口禁用。
- ConfigurationGuardTests 必须守卫 `AiReadController` 的 GET-only 表面、`production-records` 唯一生产数据入口、`AuthorizeAiRead` 权限声明和 raw `payload_jsonb` 禁止暴露。
- AICopilot 如需新增读取域，必须先在 Cloud 增加正式 AI Read API；不得用 MCP、Tool、Agent workflow、Text-to-SQL 或后台任务绕过本契约直连生产库。
