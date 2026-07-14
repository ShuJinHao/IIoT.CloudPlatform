# Cloud 缓存韧性契约

本文档是 `CLOUD-CACHE-001` 的唯一长期规则正文。规则索引、云端规则、治理清单和历史复盘只能链接本锚点，不得复制或改写为第二份正文。

<a id="cloud-cache-001"></a>
## CLOUD-CACHE-001

> 缓存故障可以受控降级；调用方取消和 factory 业务异常必须传播，降级不得导致 factory 重复执行。

范围元数据（不是第二份规范正文）：适用于 Cloud 中 Redis、FusionCache 及其包装器的 `GetAsync`、`SetAsync`、`RemoveAsync`、`RemoveByPatternAsync`、`GetOrSetAsync`。“缓存基础设施故障”只指能够同时证明发生在 provider 阶段、来源属于 Redis/FusionCache/backplane 且符合批准分类的故障，不包含调用方取消、factory 抛出的异常或 factory 内数据库/业务异常。分布式锁、租约、幂等、权限及其他既有 fail-closed 语义不在本规则范围内。

## 异常来源分类

| 来源 | 必须行为 | 禁止行为 |
| --- | --- | --- |
| 调用方 cancellation token | 传播 `OperationCanceledException`/派生取消异常，保留调用方 token 语义 | 记 warning 后返回 default、返回成功、继续扫描/删除或发起 factory |
| factory/数据库/业务逻辑 | 传播同一异常实例；factory 一次请求最多执行一次 | 按异常类型或消息把 factory 异常猜成缓存故障，或在 catch 中再执行 factory |
| Redis/FusionCache/provider 基础设施 | 只按明确的 API 降级契约返回 default/继续 factory/忽略 cache mutation，并记录脱敏日志 | 覆盖已发生的 factory 业务异常，或把未知异常默认当成可降级故障 |
| 未知/无法确认来源 | 原样传播 | 宽泛 `catch (Exception)` 后吞掉 |

异常类型本身不能证明来源。Redis/timeout/cache-like 类型若由 factory 抛出，仍按 factory 业务异常处理；序列化、配置、服务端命令、未知 provider 异常在没有明确批准前也不得自动降级。当前仓库没有 `packages.lock.json`，`StackExchange.Redis` 只有传递依赖范围，因此后续实现批次必须按实际解析版本和调用阶段复核可降级异常清单，不能在本契约中猜测一个已锁定的精确版本。

## 当前 base 事实与未决设计债务

本节记录 base `88c41109fbcf0b87b18939a139e0bff751e03d07` 的审计事实和后续实现约束，不是第二份长期规则正文：

- 当前 `RedisCacheService` 在 `GetAsync`、`SetAsync`、`RemoveAsync` 和 pattern 路径使用宽泛 catch；`GetOrSetAsync` 把 provider、factory 与回填放在同一 catch 中，并可能在异常后再次执行 factory。当前实现不符合 `CLOUD-CACHE-001`。
- FusionCache 全局启用了 fail-safe，`GetOrSetAsync` 当前用于权限和设备授权缓存；不能在未做调用点审计时把 stale fail-safe 外推为所有调用点都允许。
- 当前没有 `RedisCacheServiceTests`，也没有真实 Redis 集成测试项目。现有配置文本检查、Recording fake 和缓存 key/TTL happy path 不能作为本债务的实施或验证证据。
- 普通 cache-aside `GetAsync` 的已批准 provider 读故障候选策略是 miss + warning；权威数据读取完成后的普通 `SetAsync` 写故障候选策略是 best-effort + warning。
- 容量报表和普通展示/列表投影的失效候选可以 best-effort；权限、设备身份和配方等安全敏感失效不得直接套用普通 best-effort。安全敏感失效只有在“已完成删除”或“已进入能够阻止陈旧读取的受批准可靠状态”时才能对调用方报告成功，并要分别记录业务变更和失效/恢复状态。
- `DeviceCode`、`DevicesByProcess`、`RecipesByProcess` 在当前 base 只有 remove 调用而没有成对 reader/writer；实现前必须先完成调用点清理裁决，不能仅凭 key 名猜测降级语义。

## `CLOUD-CACHE-TEST-DEBT-001` 自动门禁矩阵

下表是债务验收元数据，不改写上述唯一长期规则正文。目标测试资产固定为 `RedisCacheServiceTests`；目标 required 工作流为 `.github/workflows/cloud-ci.yml` 的 `build-test`。当前每项均为 `not-implemented / not-verified / not-base-owned / not-source-bound`。

| 组别 | 待实施精确测试方法 | 验收行为 |
| --- | --- | --- |
| provider await 取消 | `GetAsync_CallerCancellationDuringProviderAwait_Propagates` | `TryGetAsync` await 阶段取消必须传播，不得返回 default |
| provider await 取消 | `SetAsync_CallerCancellationDuringProviderAwait_Propagates` | `SetAsync` await 阶段取消必须传播，不得伪成功 |
| provider await 取消 | `RemoveAsync_CallerCancellationDuringProviderAwait_Propagates` | `RemoveAsync` await 阶段取消必须传播，不得伪成功 |
| provider 读故障 | `GetAsync_ApprovedProviderFailure_DegradesToMissAndLogs` | 只有已批准且能证明来自 provider 读取阶段的故障可降级为 miss，并写脱敏 warning |
| provider 写故障 | `SetAsync_ApprovedProviderFailure_UsesApprovedBestEffortOrFailClosedAndLogs` | 按调用点批准策略 best-effort 或 fail-closed；无策略、未知来源和取消必须传播 |
| provider 删除故障 | `RemoveAsync_ApprovedProviderFailure_UsesApprovedBestEffortOrFailClosedAndLogs` | 普通投影与安全敏感失效分别执行已批准策略，不得无条件伪成功 |
| `GetOrSetAsync` 前置取消 | `GetOrSetAsync_TokenAlreadyCancelled_DoesNotInvokeFactory` | 入口 token 已取消时 factory 调用次数为 0 |
| factory 开始前取消 | `GetOrSetAsync_CancelledBeforeFactoryStart_DoesNotInvokeFactory` | provider 返回前取消后不得再发起 factory |
| factory 执行中取消 | `GetOrSetAsync_CallerCancellationDuringFactory_PropagatesSameCancellation` | factory 观测同一 token，取消原样传播，调用次数为 1 |
| factory 完成后取消 | `GetOrSetAsync_CancelledAfterFactoryCompletion_DoesNotReinvokeFactory` | factory 已完成后即使回填阶段取消，factory 仍只能执行 1 次 |
| factory 业务异常 | `GetOrSetAsync_FactoryBusinessException_PropagatesSameInstanceOnce` | 使用 `ReferenceEquals` 证明同一异常实例，factory 调用次数为 1 |
| cache-like factory 异常 | `GetOrSetAsync_FactoryThrowsRedisLikeException_DoesNotTreatItAsProviderFailure` | factory 自己抛出 Redis/timeout/cache-like 类型时，必须按 factory 来源传播同一实例，不得再执行 |
| provider 故障 + factory 异常 | `GetOrSetAsync_ProviderFailureThenFactoryFailure_PreservesFactoryException` | 先发生真实 provider 故障再执行 factory 时，factory 异常必须成为最终异常，不得被 cache 异常覆盖 |
| provider 故障 + factory 成功 | `GetOrSetAsync_ProviderFailureBeforeFactory_FactorySucceedsOnce` | 已批准 provider 故障可进入 factory；factory 成功时调用次数精确为 1，不得从 catch 再执行 |
| 未知异常 | `GetOrSetAsync_UnknownProviderException_Propagates` | 无法确认为可降级缓存基础设施故障时原样传播 |
| `SetAsync(null)` | `SetAsync_NullValue_DelegatesToRemoveAndPropagatesCancellation` | null 语义只执行一次 remove，传播 remove 阶段取消/未知异常，不得记录成功 |
| pattern endpoint 枚举 | `RemoveByPatternAsync_CancelledDuringEndpointEnumeration_Propagates` | 在 `GetEndPoints`/节点枚举边界观测取消，不得继续下一节点 |
| pattern scan | `RemoveByPatternAsync_CancelledDuringScan_PropagatesAndStops` | `KeysAsync`/SCAN 期间取消必须传播，不得被节点内 catch 吞掉 |
| pattern remove | `RemoveByPatternAsync_CancelledDuringRemove_PropagatesAndStops` | 逐 key `RemoveAsync` 期间取消必须传播，不得继续后续 key/节点 |
| pattern 真实节点故障 | `RemoveByPatternAsync_DisconnectedEndpoint_LogsAndContinuesConnectedEndpoints` | 只对明确断连节点执行契约中的受控降级，不覆盖取消 |
| pattern provider 故障 | `RemoveByPatternAsync_ApprovedEndpointFailure_UsesApprovedBestEffortOrFailClosedAndLogs` | 逐 endpoint/scan/remove 保留调用点策略；安全敏感 pattern 不得因单节点失败伪装为全部失效成功 |
| Redis timeout | `GetOrSetAsync_RedisTimeout_InvokesFactoryAtMostOnce` | 确认来自 provider 的 timeout 可降级到 factory，factory 仅 1 次 |
| Redis recovery | `GetOrSetAsync_AfterRedisRecovery_UsesProviderWithoutDuplicateFactory` | 断连/超时后恢复连接的下一次请求不保留错误降级状态，不重复 factory |
| 权限 stale fail-safe | `GetOrSetAsync_PermissionCache_DoesNotReturnStaleValueAfterSecurityInvalidationFailure` | 权限/设备授权失效失败后不得用全局 fail-safe 返回陈旧授权结果 |
| 登录发 token | `PermissionInvalidation_ApprovedProviderFailure_DoesNotMintTokenFromStaleEntry` | 登录/刷新链的安全敏感失效未完成且未进入可靠阻断状态时，不得从陈旧权限条目铸发 token |
| 定义角色取消 | `DefineRolePolicy_CallerCancellationDuringInvalidation_Propagates` | 调用方在失效阶段取消必须传播，不能转换成普通失败结果或继续后续动作 |
| 安全敏感失效恢复 | `SecuritySensitiveInvalidation_CompletesOrRecordsReliableBlockingState` | 成功只代表已删除或已记录可阻止陈旧读取的可靠状态；业务变更与恢复状态分别可审计 |
| 单次执行总守卫 | `GetOrSetAsync_AllFailureInterleavings_InvokeFactoryAtMostOnce` | 穷举 provider 前/中/后失败与 cancellation 组合，一次 API 调用的 factory count 始终 `<= 1` |

## 实施与信任状态

- 债务编号：`CLOUD-CACHE-TEST-DEBT-001`。
- `ruleIsNormative=true`；这是规则效力，不等同于 required CI 已生效。
- `requiredCi.normativelyRequired=true`；这是目标门禁分类，不等同于已有实现或远端 required context。
- `implemented=false`、`verified=false`、`baseOwned=false`、`sourceBound=false`。
- 人工验收：在债务关闭前，reviewer 必须逐项核对异常来源、异常实例、factory 计数和 cancellation token；宽泛 catch 不能作为通过证据。
- 未实施全部方法、未把该测试资产纳入 `cloud-ci / build-test`、未验证 base ownership/source binding 前，不得宣称 required 门禁生效。
