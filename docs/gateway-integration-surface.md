# 云端联调入口表

当前正式联调入口固定为：

- `human`：人员端后台与 Web 管理请求
- `edge`：已认证设备的正式业务请求
- `bootstrap`：匿名设备引导请求
- `ai-read`：AI service account 只读查询请求

正式路由面统一为：

- `/api/v1/human/*`
- `/api/v1/edge/*`
- `/api/v1/bootstrap/*`
- `/api/v1/ai/read/*`

## 正式入口

| 入口面 | 典型用途 | 正式路径 | 认证语义 |
| --- | --- | --- | --- |
| `human` | 人员登录、后台管理、主数据、设备管理 | `/api/v1/human/*` | Human JWT |
| `edge` | 设备产能、日志、过站、配方读取 | `/api/v1/edge/*` | Edge JWT + device binding |
| `bootstrap` | 设备冷启动、匿名引导、edge-login | `/api/v1/bootstrap/device-instance` `POST /api/v1/bootstrap/edge-login` | AllowAnonymous + bootstrap 限流 |
| `ai-read` | AI 只读摘要、产能、日志、过站查询 | `/api/v1/ai/read/*` | AI service account JWT + `AiRead.*` 权限 |

## 兼容 alias（deprecated）

以下旧路径仍可用，但只用于过渡兼容，不应再新增依赖：

| 旧路径 | 替代正式路径 | 状态 |
| --- | --- | --- |
| `/api/v1/edge/bootstrap/device-instance` | `/api/v1/bootstrap/device-instance` | deprecated |
| `/api/v1/human/identity/edge-login` | `/api/v1/bootstrap/edge-login` | deprecated |

## AiRead service account 边界

- `/api/v1/ai/read/*` 只接受 Cloud 信任签发端签发的 AI service account JWT，令牌必须包含 `actor_type=ai-service-account` 以及对应 `AiRead.*` 权限点。
- `AiRead.*` 权限只用于 AI service account，不应分配给 human 角色，也不能替代 human RBAC 或 edge device binding。
- 生产启用前必须完成 service account 签发、轮换和撤销运维方案；如果没有单令牌撤销清单，撤销依赖禁用账号或轮换凭据，并用短有效期令牌控制风险窗口。
- `delegated_user_id` 和 `delegated_device_id` 是可选范围声明。存在 `delegated_device_id` 时，Cloud 只返回这些设备范围内的数据；不存在时表示 service account 按自身 `AiRead.*` 授权读取接口允许范围，不继承 human 用户权限。
- 无设备范围的 AiRead token 只允许发给经过批准的系统级只读任务，不能作为 AICopilot 默认调用凭据。

## 使用规则

- 新接入一律只走正式入口，不允许再依赖 deprecated alias。
- `bootstrap` 的语义固定为“匿名设备引导”，不是“已认证 edge”。
- `edge-login` 已归入 `bootstrap` 面，只是当前后端内部仍通过 Gateway 做路径重写兼容。
- `ai-read` 只允许读取 Cloud 主动暴露的只读契约，不复用 human DTO，不提供写接口。
- 当兼容 alias 的命中统计清零后，再单开批次移除旧路径。
