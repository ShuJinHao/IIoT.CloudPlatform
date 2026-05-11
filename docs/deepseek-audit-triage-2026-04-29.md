# DeepSeek 全量审计归并记录

审计日期：2026-04-29

适用范围：`IIoT.CloudPlatform`。本记录只归并云端问题，不把 `IIoT.EdgeClient` 的协议升级、桌面端存储或部署证书项混入本轮代码收尾。

## 归并原则

- 不照单全收风险等级。优先核对源码、现有 PR、业务规则和“不能破坏旧 EdgeClient 启动”的约束。
- 只把能在云端独立闭环的问题纳入收尾 PR。
- 需要 EdgeClient、证书基础设施、BFF/httpOnly Cookie、审计事务模型或缓存可靠性设计配合的项单独立项。
- EdgeClient 已完成启动密钥接入，`BootstrapAuth:RequireSecret=true` 是当前强制默认值；不再保留无密钥启动或旧路径兼容口径。

## PR-15 本批处理

| 编号 | 归并结论 | 处理 |
| --- | --- | --- |
| A01 Bootstrap RequireSecret=true | EdgeClient 已升级为携带 `X-IIoT-Bootstrap-Secret`，云端不再允许无密钥启动。 | 源码默认值、生产 compose 和 `.env.example` 均固定为 `true`；部署时必须把客户端 `CloudApi:BaseUrl` 指向 Gateway，并配置云端生成的启动密钥。 |
| B09 删除设备不清除 Bootstrap 缓存 | 确认问题。兼容模式下删除设备后，`DeviceCode` 缓存可能继续签发 JWT。 | 删除设备成功后清除 `CacheKeys.DeviceCode(device.Code)`。 |
| B10 RotateDeviceBootstrapSecret 权限声明不一致 | 确认需要明确语义。该操作属于管理员级安全操作。 | 保持 Handler 内 Admin 强校验，并补测试锁定“非 Admin 即使通过普通设备权限也不能轮换”的行为。 |
| B11 GatewayRouteCatalog 缺少 bootstrap-edge-refresh | 确认问题。YARP 路由存在，但观测 catalog 未识别。 | 补 `/api/v1/bootstrap/edge-refresh` 到网关路由 catalog。 |
| B20 aspirate-output/.env 包含生产密钥 | 本仓库未跟踪 `aspirate-output/.env`。 | 作为本地安全检查项，不提交真实 `.env`；示例文件继续使用占位符。 |

## PR-16 计划处理

| 编号 | 归并结论 | 计划 |
| --- | --- | --- |
| A07 基础设施容器无资源限制 | 确认生产稳定性问题。 | 给 `postgres`、`redis-cache`、`rabbitmq`、`seq`、`nginx-gateway` 增加 `mem_limit` 和 `cpus`。 |
| A09 上传端点无请求体大小限制和批量上限 | 确认 DoS 风险。 | 给上传端点加请求大小限制、批量上限和字段校验。 |
| B04 上传数据零校验 | 确认输入质量问题。 | 给设备日志、小时产能、注塑过站、码垛过站 Command 增加 FluentValidation。 |
| B14 Gateway YARP 集群无健康检查 | 确认部署韧性问题。 | 配置 HttpApi active health check，目标 `/internal/healthz`。 |

## PR-17 计划处理

| 编号 | 归并结论 | 计划 |
| --- | --- | --- |
| A03 Outbox 达到 MaxAttempts 后静默排除 | 确认一致性和运维可见性问题。 | 增加 abandoned/dead-letter 状态或索引，让失败消息可查询、可告警。 |
| A06 Recipe.MarkDeleted 可删除 Active 配方 | 确认业务守卫缺失。 | 删除前要求配方已归档，Active 配方禁止删除。 |
| A10 Outbox Dispatcher 水平扩展重复派发 | 确认并发风险。 | 用 PostgreSQL 互斥领取待派发消息，避免多个 DataWorker 同时处理同一条 Outbox。 |
| A11 API 错误响应格式不统一 | 确认 API 契约问题。 | Result 错误路径统一返回 ProblemDetails，保留 HTTP 状态码语义。 |
| B01 MfgProcess 缺少 RowVersion | 确认聚合并发保护不一致。 | 增加 xmin RowVersion 映射和迁移。 |
| B08 DeviceLog 去重 DateTime 标准化不一致 | 确认一致性风险。 | 统一 `DateTimeKind.Unspecified` 的 UTC 处理策略。 |

## 降级或后续设计

| 编号 | 归并结论 | 原因 |
| --- | --- | --- |
| A02 RefreshToken 轮换并发 | 降级为测试和错误消息硬化。 | 当前 PostgreSQL `xmin` 已能捕获并发更新冲突；不按“高危失效”处理。后续补并发测试和并发冲突提示。 |
| A04 DomainEvent SchemaVersion | 后续事件版本化设计项。 | 领域事件版本化会影响 Outbox 历史消息、事件处理器和反序列化策略，不塞进收尾 PR。 |
| A05 审计轨迹双写 | 后续审计事务模型重构。 | 需要统一审计写入与业务 DbContext/UnitOfWork 的关系，不能靠局部补丁闭环。 |
| A08 JWT 存 localStorage | 需前端和认证架构配合。 | httpOnly Cookie/BFF 会改变 Web 会话模型，不是云端单独小改。 |
| B05 缓存失效静默失败 | 后续缓存可靠性方案。 | 需要缓存失效 Outbox、重试或告警设计，不在本批收尾中扩大。 |
| B12 员工与身份跨聚合事务 | 需确认 ASP.NET Identity DbContext 和事务边界。 | 暂不直接改身份主链路。 |
| B15 生产无 TLS | 部署基础设施项。 | 需要证书、域名和网关部署策略，代码仓库不伪造证书配置。 |
| B17 Device 状态硬编码 | 按当前云端模型降级为前端/产品需求项。 | 目前云端设备是硬删除模型，没有停用字段；不能凭前端显示直接要求云端补状态。 |
| B18/B19 并发和中间件故障测试不足 | 测试增强项。 | 价值明确，但不作为阻塞 PR-15 的代码风险。 |
| B21 UploadReceiveRegistration RowVersion | 低优先级并发计数问题。 | 唯一约束保证幂等主语义，`SeenCount` 精确性后续单独处理。 |

## 低风险项处理策略

- C01-C04：领域事件字段、重试退避等归入后续架构/可观测性优化。
- C05-C11：死代码、缓存键、审计时间类型等作为代码清理积压项处理。
- C12-C17：前端 XSS、alert、密码策略和开发密钥加密需要单独安全清理 PR，不混入云端收尾。
- C18-C31：权限声明、nginx 差异、Docker 构建缓存、迁移 Down、OTLP、测试工程化等作为后续维护项排期。

## 收尾顺序

1. PR-15：本归并文档 + 低风险安全收口。
2. PR-16：输入限制与生产稳定性。
3. PR-17：Outbox / 并发 / API 清理。

完成以上三批后，云端主链路整改可以进入收尾状态；剩余项不再作为“云端必须立即修完”的无限扩展范围。
