# 云端联调入口表

当前正式联调入口固定为：

- `human`：人员端后台与 Web 管理请求
- `edge`：已认证设备的正式业务请求
- `bootstrap`：匿名设备引导请求

正式路由面统一为：

- `/api/v1/human/*`
- `/api/v1/edge/*`
- `/api/v1/bootstrap/*`

## 正式入口

| 入口面 | 典型用途 | 正式路径 | 认证语义 |
| --- | --- | --- | --- |
| `human` | 人员登录、后台管理、主数据、设备管理 | `/api/v1/human/*` | Human JWT |
| `edge` | 设备产能、日志、过站、配方读取 | `/api/v1/edge/*` | Edge JWT + device binding |
| `bootstrap` | 设备冷启动、匿名引导、edge-login | `/api/v1/bootstrap/device-instance` `POST /api/v1/bootstrap/edge-login` | AllowAnonymous + bootstrap 限流 |

## 兼容 alias（deprecated）

以下旧路径仍可用，但只用于过渡兼容，不应再新增依赖：

| 旧路径 | 替代正式路径 | 状态 |
| --- | --- | --- |
| `/api/v1/edge/bootstrap/device-instance` | `/api/v1/bootstrap/device-instance` | deprecated |
| `/api/v1/human/identity/edge-login` | `/api/v1/bootstrap/edge-login` | deprecated |

## 使用规则

- 新接入一律只走正式入口，不允许再依赖 deprecated alias。
- `bootstrap` 的语义固定为“匿名设备引导”，不是“已认证 edge”。
- `edge-login` 已归入 `bootstrap` 面，只是当前后端内部仍通过 Gateway 做路径重写兼容。
- 当兼容 alias 的命中统计清零后，再单开批次移除旧路径。
