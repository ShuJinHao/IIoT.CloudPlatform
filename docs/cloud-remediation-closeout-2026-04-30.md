# 云端整改收口记录

记录日期：2026-04-30

适用范围：`IIoT.CloudPlatform`。本记录只用于确认云端整改是否可以收口，不包含 `IIoT.EdgeClient`、BFF/httpOnly Cookie、TLS 证书基础设施或后续身份域重构。

## 收口结论

云端主链路整改已经完成，可以进入收口阶段。

- `main` 已同步到 `origin/main`。
- PR #8 到 PR #17 已合并。
- DeepSeek 审计中“云端可以独立闭环、且不破坏旧 EdgeClient”的主要问题已处理。
- 剩余问题属于跨端、部署基础设施、架构设计或低风险维护项，不再作为本轮云端收口阻塞项。

## 后续业务接入边界

云端主体能力已经完成封装并进入封存基线。后续新增需求默认不再重开云端主链路整改，只按具体业务数据接入处理。

- 新增叠片工序、扩展注塑工序或接入其他生产业务时，先明确工序、设备、字段、单位、来源、校验规则和查询口径。
- 云端侧只新增必要的 DTO、Command、Validator、事件契约、持久化表、查询接口和前端展示字段。
- Bootstrap、设备身份、上传幂等、Outbox、缓存边界、ProblemDetails 错误响应、部署健康检查和资源限制均沿用当前封存后的通用能力。
- 不允许因为新增一个工序或字段，重新改造云端身份链路、上传主链路、消息派发主链路或部署主链路。
- 如果新增业务字段需要 EdgeClient 同步上传，必须先形成接口字段对齐说明，再分别在 EdgeClient 和 CloudPlatform 中按独立任务实现。

## 已合并整改批次

| PR | 主题 | 主要内容 |
| --- | --- | --- |
| #8 | cloud remediation batch 2 | 刷新令牌并发、事件版本守卫、EF 更新行为、容量发布顺序、DataWorker healthcheck、Bootstrap 后续设计准备。 |
| #9 | aggregate domain events | 补齐设备改工序、工序重命名等关键聚合领域事件。 |
| #10 | integration event boundary | 建立 `IIntegrationEvent` 类型边界，限制消息总线发布对象。 |
| #11 | integration events outbox | 上传链路集成事件进入 Outbox，由 dispatcher 派发。 |
| #12 | upload receive idempotency | 上传接收登记和幂等，避免重复上传重复写 Outbox。 |
| #13 | boundary cleanup | 缓存失效边界、Gateway 异常、Bootstrap header filter、中文错误和 ValidationBehavior 清理。 |
| #14 | bootstrap secret hardening | 预共享启动密钥云端能力，数据库仅保存哈希，支持轮换。 |
| #15 | audit triage closeout | DeepSeek 审计归并文档、删除设备清 Bootstrap 缓存、密钥轮换权限语义、Gateway 路由识别。 |
| #16 | input and deployment stability | 上传请求大小限制、批量上限、字段校验、基础设施资源限制、YARP health check。 |
| #17 | outbox concurrency and API cleanup | Outbox abandoned 状态、PostgreSQL `SKIP LOCKED`、Active Recipe 删除守卫、`MfgProcess` 并发映射、ProblemDetails。 |

## 已闭环的主要问题

- Bootstrap：云端已支持启动密钥、密钥哈希保存、密钥轮换、强制密钥校验和删除设备后的缓存失效。
- 上传：设备日志、小时产能、注塑过站、码垛过站已具备请求大小限制、批量上限、字段校验、接收登记和幂等去重。
- 事件：跨服务事件有类型边界，集成事件不再直接绕过 Outbox，Outbox 派发失败耗尽后可见化。
- 并发：刷新令牌和关键聚合使用 PostgreSQL `xmin`；Outbox dispatcher 使用 `FOR UPDATE SKIP LOCKED` 避免多 worker 重复派发。
- 聚合：设备改工序、工序重命名等关键领域事件已补齐；Active 配方禁止物理删除。
- API/部署：Result 失败响应统一 `ProblemDetails`；DataWorker 有 healthcheck；YARP 有主动健康检查；生产 compose 有资源限制。

## 延期且不阻塞收口的事项

| 项目 | 当前处理 |
| --- | --- |
| Bootstrap 强制 `RequireSecret=true` | EdgeClient 已完成启动密钥接入，云端默认强制校验；不再保留无密钥启动或旧路径兼容口径。 |
| RefreshToken `SELECT FOR UPDATE` | 当前 PostgreSQL `xmin` 已提供并发冲突保护，暂不做事务锁重构。 |
| DomainEvent `SchemaVersion` | 属于领域事件版本化设计，涉及历史 Outbox 消息兼容，后续单独设计。 |
| 审计双写 | 需要统一审计写入和业务 DbContext/UnitOfWork 关系，后续单独设计。 |
| 缓存失效可靠性 | 需要缓存失效 Outbox、重试或告警策略，后续单独设计。 |
| JWT localStorage | 需要前端和 BFF/httpOnly Cookie 会话模型配合，不属于云端单独闭环。 |
| TLS/证书 | 需要域名、证书和部署网关基础设施，不在代码仓库中伪造配置。 |
| 设备停用状态 | 当前云端设备模型是硬删除，没有停用字段；如产品需要停用，后续按功能需求单独设计。 |
| OpenTelemetry `NU1902` | 单独依赖升级维护项。 |
| 前端 CSS minify warning | 单独前端维护项。 |

## 发布前固定验证

发布或合并重要云端改动前，固定执行以下验证：

- `dotnet build IIoT.CloudPlatform.slnx -c Release`
- `dotnet test src/tests/IIoT.ServiceLayer.Tests/IIoT.ServiceLayer.Tests.csproj -c Release`
- GitHub `cloud-ci / build-test`，包含端到端测试
- `npm run build`
- `docker compose --env-file deploy/.env.example -f deploy/docker-compose.prod.yml config -q`

## 后续工作原则

- 不再新增“云端架构整改”大 PR。
- 后续只接受小型维护 PR，例如依赖告警、前端构建 warning、文档补充。
- 任何会影响 Edge 启动协议、身份会话模型、审计事务模型、缓存可靠性或 TLS 部署的事项，必须单独立项并重新评估范围。
