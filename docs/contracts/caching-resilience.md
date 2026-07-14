# CLOUD-CACHE-001 普通值缓存韧性契约

> 缓存故障可以受控降级；调用方取消和 factory 业务异常必须传播，降级不得导致 factory 重复执行。

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

`GetOrSetAsync` 当前没有生产调用方，但其公共合同和异常语义必须由 required 测试保持可用。`PermissionProvider`、`DevicePermissionService`、`DeviceIdentityQueryService` 是明确的安全敏感直接读取边界，不得重新接入值缓存；设备身份边界还必须由真实 PostgreSQL + Dapper required 测试证明 missing/current、底层更新后二次读取和调用方取消行为。

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
| `GetAsync` 命中白名单基础设施异常 | 记录脱敏降级日志并按 miss 返回 `default` |
| `SetAsync` / `RemoveAsync` 命中白名单基础设施异常 | 记录脱敏降级日志后返回 |
| 普通值操作遇到未知或序列化异常 | 原样传播 |
| `SetAsync` 的值为 `null` | 只调用同 key 的 `RemoveAsync`，不得写入 null |
| `RemoveByPatternAsync` endpoint/scan 遇到 Redis 连接或超时 | 记录脱敏日志并结束当前可降级范围 |
| `RemoveByPatternAsync` 遇到未知异常 | 原样传播 |
| endpoint lookup 后、节点扫描中、枚举 key 中或逐 key 删除中取消 | 立即传播取消，不得继续扫描/删除 |

所有操作均必须向底层传递调用方 token。任何 `OperationCanceledException` 均不得被基础设施降级 catch 吞掉；若底层或 factory 抛出具体取消异常实例，调用方应观察到同一实例。

## 3. `GetOrSetAsync` 单次 factory 规则

每次服务调用至多启动一次 factory；即使 provider 重复调用 delegate、provider 与 factory 竞态或缓存 write-back 失败，也不得启动第二次回源。

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
| write-back 阶段发生调用方取消 | 传播同一取消异常，不重复 factory |
| 等待同一 factory 时调用方取消，即使 factory 忽略 token | 调用方等待立即取消；后台 factory 仍不得被第二次启动 |

factory 的业务失败优先于同时出现的可降级 provider 失败；缓存异常不得遮蔽已经捕获的 factory 异常。

## 4. 日志与可观测性

缓存降级统一使用 `ValueCacheInfrastructureDegraded (EventId 2401)`。日志只允许记录稳定 operation 分类和异常类型名；不得记录 cache key、pattern、Redis endpoint、原始 exception message、exception 对象、业务 DTO 或 stack trace。

## 5. Required 验证

- `IIoT.CloudPlatform.WorkflowTests` 中的 37 条确定性语义测试覆盖本契约的白名单、未知异常、取消点、factory 单次执行和同实例传播。
- `IIoT.CloudPlatform.IntegrationTests` 必须直接进入 `cloud-ci / build-test`，使用固定镜像 `redis@sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0dcda72dbb3c32061ab99`，真实验证 pause/unpause、stop/start、断连降级、恢复、factory/fallback 单次执行和 pattern 删除故障。测试必须创建两个彼此独立、均由生产 `AddInfrastructures` 接线的 service provider/runtime，并分别解析 `ICacheService`、`IFusionCache` 和 `IConnectionMultiplexer`；同一 key 必须先让 reader 的 L1 持有旧值，再由 writer 更新，最终证明 backplane 让 reader 观察到新值。删除或绕过 backplane 接线必须导致该 required 测试失败。
- 两组必须分别精确对账为 37/37 与 1/1，`failed = 0`、`notExecuted/Skip = 0`；Docker/Redis 不可用必须失败，不能 Skip 或改用环境变量替换镜像。
