# CLOUD-CACHE-001 普通值缓存韧性契约

> 缓存故障可以受控降级；调用方取消和 factory 业务异常必须传播，降级不得导致 factory 重复执行，并发失效后旧 factory 不得回填缓存。

## 1. 适用边界

`ICacheService` 只用于“缓存不可用时仍可从正式数据源读取或继续业务”的普通值缓存。当前允许的数据类别只有：

- 权限定义展示目录 `AllDefinedPermissions`；它不是用户授权判定结果。
- 工序、设备和配方的查询 DTO。
- 小时产能、汇总、范围和分页查询 DTO。

权限判定、设备访问范围和设备身份必须直接读取正式身份/业务存储并 fail closed。Redis 分布式租约、幂等登记、Outbox 和其它一致性敏感状态继续使用各自专用合同；不得通过 `ICacheService` 复用本契约的降级语义。

当前生产调用点已经逐项分类：

| 类别 | 读取/回填 | 失效 | 判定 |
| --- | --- | --- | --- |
| 权限定义展示目录 | `GetAllDefinedPermissions` | `DefineRolePolicy`、`UpdateRolePermissions` | 允许；只影响展示目录，不参与授权 |
| 工序目录 | `GetAllProcesses` | `CreateProcess`、`UpdateProcess`、`DeleteProcess` | 允许 |
| 设备查询 DTO | `GetAllDevices` | `DeviceCacheInvalidationService` | 允许 |
| 配方查询 DTO | `GetRecipeById`、`GetRecipesByDeviceId` | `RecipeCacheInvalidationService`、`DeviceCacheInvalidationService` | 允许 |
| 产能查询 DTO | Human/Edge 的 `GetSummaryByDeviceId`、`GetSummaryRange`，Human 的 `GetDailyCapacityPaged` | `ReceiveHourlyCapacity`、`PersistHourlyCapacity`、`DeviceCacheInvalidationService` | 允许 |

上述分类对应 10 个生产 `GetOrSetAsync` 调用点：权限 1、工序 1、设备 1、配方 2、产能 5。产能 4 个 Human/Edge summary/range caller 在同一 `ProductionService/Queries/Capacities` bounded context 内共用 admission/key/TTL 策略，共有 4 个真实 caller；Human 授权仍在该策略之前执行，4 个 handler 仍由 caller matrix 直接锁定 null/空 admission 与 token。该同域共享实现不是兼容 adapter/wrapper。`ICacheService` 只保留 `GetOrSetAsync`、`RemoveAsync` 和 `RemoveByPatternAsync`；已经没有真实生产调用方的分离 `GetAsync`/`SetAsync`、默认 TTL overload 与实现必须物理删除，不得保留 adapter、wrapper 或 fallback。`PermissionProvider`、`DevicePermissionService`、`DeviceIdentityQueryService` 是明确的安全敏感直接读取边界，不得重新接入值缓存；设备身份边界还必须由真实 PostgreSQL + Dapper required 测试证明 missing/current、底层更新后二次读取和调用方取消行为。

## 2. 异常与取消矩阵

普通值操作的可降级基础设施异常白名单精确为：

- `FusionCacheDistributedCacheException`
- `FusionCacheBackplaneException`
- `SyntheticTimeoutException`
- `RedisConnectionException`
- `RedisTimeoutException`

白名单之外的异常，包括序列化/反序列化错误和未知 provider 异常，必须原样传播；不得因为发生在缓存适配层就吞掉。`RemoveByPatternAsync` 的 endpoint/scan 白名单更窄，只允许 `RedisConnectionException` 和 `RedisTimeoutException` 降级。

| 场景 | 必须行为 |
| --- | --- |
| 入口 token 已取消 | 在接触 FusionCache、Redis 或 factory 前抛取消异常 |
| `GetOrSetAsync` 在 factory 启动前命中白名单 provider 异常 | 记录脱敏降级日志，以同一 caller token 回源一次 |
| `GetOrSetAsync` 在 factory 成功后命中白名单 write-back 异常 | 返回已取得结果，不重复 factory |
| `RemoveAsync` 的 generation bump 命中 Redis 连接/超时 | 在物理删除前传播原异常；不得返回失效成功 |
| `RemoveAsync` 已成功 bump 后的物理删除命中白名单基础设施异常 | 记录脱敏降级日志 |
| 普通值操作遇到未知、序列化或 admission policy 异常 | 原样传播；不得改写为缓存降级 |
| `RemoveByPatternAsync` 的 generation bump 命中 Redis 连接或超时 | 在 endpoint/scan 前传播原异常；不得返回失效成功 |
| `RemoveByPatternAsync` endpoint/scan 遇到 Redis 连接或超时 | 记录脱敏日志并结束当前可降级范围 |
| `RemoveByPatternAsync` 遇到未知异常 | 原样传播 |
| endpoint lookup 后、节点扫描中、枚举 key 中或逐 key 删除中取消 | 立即传播取消，不得继续扫描/删除 |

普通读写、扫描和 factory 必须观察调用方 token。`GetOrSetAsync` 的 provider 等待由调用方 token 取消，但 generation 二次检查和 stale write-back 清理必须改用独立、显式有界的 cleanup token，不能因 caller 先退出而留下旧值。任何 `OperationCanceledException` 均不得被基础设施降级 catch 吞掉；若底层或 factory 抛出具体取消异常实例，调用方应观察到同一实例；外层等待取消至少必须保留原 caller token。

## 3. `GetOrSetAsync` 单次 factory 规则

每次服务调用至多启动一次 factory；即使 provider 重复调用 delegate、provider 与 factory 竞态或缓存 write-back 失败，也不得启动第二次回源。

每个调用方必须显式传入无默认值的 `shouldCache` admission policy 和正值 TTL；`RedisCacheService` 的 `IOptions<DomainEventCacheInvalidationOptions>` wrapper 及其 `Value` 也必须由 DI 显式注入并在构造时 exact non-null、fail closed。不得把 null 延迟成运行时 `NullReferenceException`，也不得通过兼容 overload、默认 predicate/TTL、构造器 fallback 或适配器隐藏决定。当前 10 个调用点的判定固定为：

- 权限定义、工序、设备、`GetRecipesByDeviceId`、`GetDailyCapacityPaged` 的非 null 列表/分页结果允许缓存；合法空结果也允许缓存。
- `GetRecipeById` 和 Human/Edge 单日 summary 只缓存非 null；NotFound/null 保持原 `Result` 语义并不得缓存。
- Human/Edge range 只缓存 `Count > 0`；空范围返回成功空列表，但不得缓存。
- `GetAllDevices` 的 admin 校验必须发生在 cache/factory 前；`GetRecipeById` 的设备 scope 校验保持在 DTO 取得后执行，cache hit 也必须再次校验；`GetDailyCapacityPaged` 的用户 scope/no-access/specific-device 校验必须发生在 cache/factory 前，非管理员聚合查询继续直接回源而不共享缓存。
- caller token 必须传给 `GetOrSetAsync`，并由同一 token 进入 repository/query factory；权限目录的底层 role API 没有 token 合同时，factory 必须在调用前及循环中显式检查取消。

| 时序 | 必须行为 |
| --- | --- |
| provider 在 factory 启动前抛白名单异常 | 以调用方 token 启动 factory 一次并返回其结果 |
| 上述 fallback factory 抛业务异常/取消 | 同一异常实例传播，factory 恰好一次 |
| factory 已成功，随后 backplane/L2 write-back 抛白名单异常 | 返回已得到的 factory 结果，不重复 factory |
| factory 正在运行，provider 抛非 synthetic 的白名单异常 | 等待同一 factory task；返回其结果或传播其同一异常 |
| factory 正在运行，provider 抛 `SyntheticTimeoutException` | 传播该 timeout，不等待越过硬超时，也不重复 factory |
| factory 抛出“看起来像缓存异常”的异常 | 仍视为 factory 失败，原样传播；不得按基础设施异常降级 |
| provider 等待 factory、捕获其取消后改抛另一个取消异常 | 优先传播 factory 捕获的原始取消异常实例，factory 恰好一次 |
| provider 抛未知或序列化异常且 factory 未启动 | 原样传播，不启动 factory |
| write-back/fence 阶段发生调用方取消 | 传播原 factory 异常实例或原 caller token；独立有界 cleanup 仍必须完成 generation 核对与 stale remove，不重复 factory |
| 等待同一 factory 时调用方取消，即使 factory 忽略 token | 调用方等待立即取消；后台 factory 仍不得被第二次启动，也不得在并发失效后回填 L1/L2 |
| factory 命中 `SyntheticTimeoutException` 后继续后台完成 | timeout 必须按时传播；本次 entry options 必须禁止 timed-out background completion 写回 |
| factory 与 exact-key/pattern 失效并发 | 原请求可观察其已开始时读取的值；失效先推进 generation，后续读取必须 miss/fresh，不能观察旧 factory 回填 |

factory 的业务失败优先于同时出现的可降级 provider 失败；缓存异常不得遮蔽已经捕获的 factory 异常。

FusionCache 2.6 的 factory wrapper 必须在值交给 provider 前二次读取 exact-key 与全局 pattern generation：generation 已变化或无法可靠验证时，对本次 `FusionCacheFactoryExecutionContext.Options` 同时设置 `SkipMemoryCacheWrite=true`、`SkipDistributedCacheWrite=true` 并跳过 backplane SET 通知。entry options 必须保持 `AllowTimedOutFactoryBackgroundCompletion=false` 和同步 distributed write。正常 provider 完成后还要用独立有界 cleanup token 再核对一次 generation，并在变化/不可验证时 remove，以覆盖“factory 二次检查之后、Fusion 实际 write 之前”的最后窗口。

## 4. 日志与可观测性

缓存降级统一使用 `ValueCacheInfrastructureDegraded (EventId 2401)`。日志只允许记录稳定 operation 分类和异常类型名；不得记录 cache key、pattern、Redis endpoint、原始 exception message、exception 对象、业务 DTO 或 stack trace。

## 5. Domain Event 缓存失效的幂等与补偿

普通 `RemoveAsync` 和 `RemoveByPatternAsync` 同样必须参与 write fence：精确删除在调用 Fusion remove 前推进该 key 的 generation；pattern 删除在 endpoint lookup/scan 前推进全局 pattern generation。这样 Process、Permission 和 Capacity 的普通失效只能在 generation 已生效时返回成功，不能被更早开始的 factory 回填。generation bump 的 Redis 连接/超时、caller cancellation 和未知异常都必须在物理 remove/scan 前原样传播；否则无法证明跨实例 fence，不得伪装失效成功。bump 成功后的 Fusion remove 或 endpoint/scan 白名单异常仍可按普通值边界脱敏降级。pattern 扫描命中的逐 key 删除可以继续推进精确 generation，但不得用它代替扫描前的全局 bump。

- 设备和配方缓存失效只由 Domain Event handler 触发；命令 handler 不得再直接删除同一组 key/pattern，不得保留双写、fallback 或旧方法别名。
- `OutboxMessage.Id` 是一次 Domain Event 派发的稳定 operation id；每个 handler 使用稳定、小写的 operation scope 形成独立 receipt。同一事务执行策略重试再次派发时，已完成 receipt 必须返回“未重复执行”，不能再做第二次逻辑失效。
- receipt/claim 必须位于 `__iiot_system:` 系统 namespace，业务 `RemoveAsync`/pattern 扫描/严格失效都不得直接或通过 glob 命中该 namespace；扫描结果中出现系统 key 也必须跳过。
- 严格失效必须在 remove 前推进 write fence：精确 key 推进该 key 的稳定 digest generation；任一 pattern 在扫描前推进全局 pattern generation。generation 与 receipt 同属受保护系统 namespace，并使用 completed retention 约束其生存期。这样即使 pattern 扫描时 in-flight key 尚未物理写入，factory 完成路径也能识别该次失效。
- claim 使用有界 lease 与续租；获取冲突可在有界 retry delay 后重试，已完成 receipt 的 retention 不得短于 7 天。claim、renew 和 complete 的 Redis 等待必须绑定原 caller token，在命令返回 completed receipt 或 complete 成功后仍必须执行取消优先级检查；不得用迟到的 commit-point 结果吞掉已观测到的 caller cancellation。续租或完成 compare-set 丢失 owner 必须 fail closed。
- 严格失效中的 Redis 断连、未知异常和取消必须传播，不复用普通值缓存的降级语义。失败路径必须用与 caller token 解耦的独立有界 cleanup token 执行 best-effort compare-delete；release 超时固定记录 `ErrorType=ReleaseCleanupTimeout`，其它 release 失败只能以 `ValueCacheInfrastructureDegraded (2401)` 记录稳定 operation 与异常类型，不得遮蔽原业务异常实例或 caller cancellation/token。进程崩溃时依靠 lease 过期允许重放补偿。这是“正常/事务重试路径单次逻辑失效 + 崩溃后可补偿重放”，不得宣称进程崩溃下 exactly-once。

## 6. 受影响验证

- 受影响 Application caller matrix 必须执行真实 handler，锁定空/null admission、原 `Result`、授权顺序和 factory token 透传。
- 受影响 Workflow 测试覆盖本契约的白名单、未知异常、取消点、factory/admission 单次执行与同实例传播、普通 exact/pattern bump-before-remove/scan，以及系统 namespace、receipt/claim、续租丢失、释放与断连传播。
- 受影响真实 Redis 测试使用固定镜像，覆盖断连恢复/backplane、claim/receipt、并发 stale-write fence、真实 handler 失效后不得回填旧 DTO，以及 null/拒绝空/允许空三类 admission。
- 本次 selector 选择的测试必须满足 `discovered = executed = passed`、`failed = 0`、`skipped = 0`。历史各层 case 数不是当前下限；Docker/Redis 不可用必须失败，不能 Skip 或换成伪集成测试。
