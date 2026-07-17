# 三项目测试架构治理总计划

> 状态：审计后重新打开（`REOPENED / NOT COMPLETE`）；2026-07-16 的 14/14、100% 仅保留为历史登记快照，2026-07-17 行级复核确认仍有阻断项，全部重新验证为 PASS 前不得关单
>
> 核心：测试单元架构、测试分层、异常与缓存语义、架构 Analyzer、重复代码和可复现 CI
>
> 适用范围：`IIoT.EdgeClient`、`IIoT.CloudPlatform`、`AICopilot` 及工作区级联合门禁
>
> 执行模型：每仓独立授权、独立批次、完整 diff、可复现 CI 和主维护者终审
>
> CI/CD 边界：CI 测试门禁属于本计划并会演进；生产交付/部署不属于本计划，现有发布入口和发布语义不在本轮重建
>
> 安全边界：禁止部署、发布、上传 `stable` 或修改生产数据，除非用户针对该次操作另行明确授权

本文件是三项目测试架构的活动执行入口。历史运行号、已退役治理偏航、临时候选、平台调查和事故过程保留在项目滚动复盘、Git 历史或 `docs/历史核心记录.md` 中，不再进入活动计划正文。

`docs/三项目源码审计与架构治理执行计划.md` 已归档为 2026-07-11 源码审计和既往批次台账，不是本计划的子执行计划，也不与本计划并行执行；其中未完成路线、部署架构和宽范围源码治理不得扩大本计划范围或计入本计划进度。

## 1. 计划定位

### 1.1 要解决的问题

三个项目并不只是“测试数量不够”，主要问题是：

- Unit、Aggregate、Application、Contract、Persistence、Workflow、Integration、UI 和 E2E 混在少数大测试项目中。
- 测试项目依赖过宽，无法从项目图证明单元测试是否纯净。
- 部分架构规则仍依赖源码字符串扫描，容易误报、漏报或被改写绕过。
- 部分测试未进入稳定 CI，或发现数、执行数、Skip 数没有统一对账。
- 缓存、取消、异常、重试、fallback 和副作用次数缺少统一且可执行的语义门禁。
- 重复 TestDoubles、fixture、DTO、helper 和生产代码缺少 baseline 与增量 ratchet。
- 历史复盘被当成默认全文必读材料，导致上下文膨胀并混淆现行规则与历史经过。

本计划的核心不是继续向旧大桶堆测试，而是让每个测试有明确职责、依赖、运行时、失败语义和 CI 归属。

### 1.2 目标

- 每个测试只有一个主要 `TestKind`，并有清晰的 Capability、Runtime、Risk、Cadence 和 Owner。
- 纯 Unit/Aggregate/Application 测试不依赖数据库、容器、Aspire、Avalonia、Browser 或真实网络。
- 静态可证明的架构违规由 Roslyn Analyzer、MSBuild graph 或 TypeScript/static gate 直接阻断 build。
- DI、EF model、HTTP metadata、migration、manifest、真实数据库等动态事实由必跑 Architecture/Integration gate 阻断。
- Required 测试满足 `discovered = executed`、`failed = 0`、`skipped = 0`。
- 缓存、异常、取消、重试和 fallback 的行为由自动化测试锁住，不能靠文档口头约束。
- 生产代码、测试基础设施和测试 case 分别建立重复代码 baseline 与增量 ratchet。
- 三仓分别清点并删除已经失去真实调用方的旧接口、alias、adapter、wrapper、fallback、双写和影子路径；兼容代码必须有当前 consumer、到期条件和测试，不能永久遗传。
- CI 命令可在本机复现；PR 总墙钟 p95 目标不超过 25 分钟。
- 历史复盘只解释“当时为什么”，正式规则和专题契约说明“现在必须做什么”。

### 1.3 非目标

- 不建设与测试架构交付无关的仓库所有权、额外人工审批或平行授权控制面。
- 不为 baseline、workflow 或 policy 另建一次性授权状态机、账本或兼容工具链。
- 不在本计划中执行生产构建、上传、发布、部署或生产状态查询。
- 不在本计划实施 Edge 宿主仓与插件仓拆分；该事项由用户在下一份独立计划中专门裁决和执行，不是当前任务、依赖或进度项。
- 不把真实 PLC、MES、Cloud、生产数据库、真实模型或 Windows 现场机塞进普通 PR。
- 不为了命名统一一次性移动全部测试，也不创建跨三仓共享的万能测试框架。
- 不用覆盖率百分比替代架构、行为、契约和故障语义测试。

### 1.4 2026-07-14 二次纠偏决定

第一次纠偏仍把错误治理资产降级为“可选保留”，没有执行用户要求的物理删除；Edge 模切也曾被错误地拆成两个入口和一个共享实现继续维护。两项都属于方向性错误，不是普通技术债。

本计划采用以下最终口径：

- `TEST-GOV-RETIRE-001`：三个仓库逐仓删除错误治理模型的可执行资产、活动规则、policy/fixture 和兼容入口；历史事实只留在明确标记为非现行的复盘和 Git 历史。
- `EDGE-DIECUT-REMOVE-001`：物理删除 `DieCutting.Shared`、`DieCuttingAnode`、`DieCuttingCathode` 及其 UI、配置、测试、打包清单和本地产物，不保留空工程、别名、stub、wrapper 或旧模块兼容。
- `EDGE-CI-REBASELINE-001`：按删除后的真实项目和测试资产重新生成 baseline；功能退役造成的测试数量下降必须列明，不得为了守旧数字保留假插件或无效测试。
- Cloud 与 AICopilot 的治理资产清理已在用户明确授权三仓写入后完成；总计划登记任务本身不自动扩大跨仓写入权限。
- 进度只按真实测试、Analyzer、分层、质量门禁和 CI 覆盖计算；错误治理工具和偏航调查不计完成度。

必须删除的错误治理遗产包括：把个人仓库迁移到 GitHub Organization/GitHub Enterprise 的方案、额外独立人员或第二 reviewer 门禁、为此建立的 CODEOWNERS 强制审批，以及 `Authorization/Consume/Cancel/TrustUpgrade + receipt + validator + wrapper + schema` 自定义授权链。这里的“删除”是从活动规则、workflow、policy、fixture、脚本和入口中物理移除，不是改名、降级为 optional，或换一套同义机制。AICopilot 产品代码中与业务含义有关的 `enterprise-ai` 等普通名称不因字符串相同而误删，删除对象必须由治理语义和真实引用证明。

精确关键词只读扫描曾确认三仓都存在遗产，现已按仓物理收口：

| 项目 | 已确认遗产 | 清理要求 |
|---|---|---|
| Edge | `.github/CODEOWNERS`、`.gitattributes` 引用、governance policy/behavior fixture、四件 baseline migration 授权链资产、RepositoryHygiene/活动文档引用 | `EDGE-GOV-PURGE-001` 已完成；文件、hash、fixture、错误消息和活动引用已删除，真实 baseline 已重建 |
| Cloud | `.github/CODEOWNERS`、`.gitattributes` 引用、policy/behavior 对 CODEOWNER 的强制规则、baseline 受保护路径、云端规则和治理清单中的独立 reviewer/branch-protection 前置 | 已删除整套审批依赖并重跑 Cloud 真实测试门禁，未保留同义 owner gate |
| AICopilot | `.github/CODEOWNERS`、`.gitattributes`/CI path 引用、policy hash/behavior fixture、AI 治理清单中的独立 reviewer/Code Owner 前置 | 已删除整套审批依赖并重跑 AI 真实测试门禁，未误删产品业务中的普通 `enterprise-ai` 名称 |

Cloud 与 AI 的初始 inventory 来自精确关键词命中，没有用全文加载历史复盘替代现行规则；获得三仓写入授权后已完成实际删除、引用复核和 CI 验证。

### 1.5 不可偏航的核心交付

“测试单元架构”是本计划主语，不只等于 `Unit` TestKind。Edge、Cloud、AICopilot 三个项目都必须完成以下五类交付，缺任一项目或任一类都不能宣布总计划完成：

| 核心主线 | Edge | Cloud | AICopilot |
|---|---|---|---|
| 测试单元与物理分层 | Architecture、Unit、Aggregate、Application、Contract、Conformance、Persistence、Workflow、Startup/UI/Deployment | Architecture、Unit、Aggregate、Application、Contract、Persistence/Integration、Workflow、Frontend/E2E | Architecture、Unit、Aggregate、Application、Workflow、Contract/Persistence/HTTP、Eval、Frontend/E2E |
| 高风险语义 | 通用插件生命周期、SQLite、DataPipeline、PLC/MES/Cloud 分离、启动非阻断 | `CLOUD-CACHE-001`、权限缓存、事务/Outbox、HTTP/Event | `AI-SEC-051-TEST`、真实 HTTP/auth/tracking、Agent 取消/补偿、Cloud 只读 |
| 架构门禁 | `EDGE-ARCH-001` + AnalyzerTests | `CLOUD-ARCH-001` + AnalyzerTests | `AI-ARCH-001` + AnalyzerTests |
| 代码治理 | 重复代码、过期兼容层、旧测试桶和重复 fixture 清理 | 重复代码、过期 endpoint/DTO/cache adapter、旧测试桶清理 | 重复代码、过期 Tool/Plugin/API/workflow adapter、旧测试桶清理 |
| CI 对账 | build、Analyzer、发现/执行/Skip、Windows/包静态门禁 | build、后端/前端门禁、发现/执行/Skip | build、后端/前端/Eval 门禁、发现/执行/Skip |

错误治理遗产清理和模切退役只是恢复正确起点，不替代上述交付，也不增加完成百分比。宿主仓/插件仓拆分属于下一份计划，不能阻塞、稀释或改写本计划主线。

## 2. 单维护者执行模型

### 2.1 普通变更闭环

workflow、policy、baseline、项目图或测试源码发生变化时，统一执行以下闭环，不另建平行授权系统：

1. 明确 Batch ID、项目、允许路径、禁止路径和本批完成定义。
2. 记录开始时的 `git status`、当前 commit、现有脏改动和完整 before inventory。
3. 删除功能时先列出源码、配置、测试、构建、打包、文档和本地产物的完整影响集合。
4. 在隔离分支或 worktree 准备候选；生产语义修复与纯 baseline 重生成尽量拆开，避免结果难以复核。
5. 复核完整 diff，并运行适用的 build、Analyzer、测试、inventory、发现/执行数量和 0 Skip 对账。
6. 记录 after inventory、数量变化和原因；功能正式退役时允许删除其专属测试，不要求用 dummy case 补回旧数字。
7. CI 验证实际候选 commit；远端 CI、artifact 和 annotations 属于本批时必须读取并对账。
8. 只有当前任务明确授权时才 commit、push、创建 PR 或修改远端设置；主维护者终审后收口。

### 2.2 写入与现场保护

- 默认一个批次只修改一个项目；用户明确授权多个项目和目录后，才可多项目并行写入。
- 每个项目使用独立 branch/worktree、独立 base SHA、独立 Batch ID 和单一写 Agent。
- 工作区共享文件只由主 Agent 修改。
- 保护既有脏工作树；禁止 reset、粗暴 checkout、覆盖用户改动或把未跟踪文件当成无关内容删除。
- 项目代码、测试、workflow 和 policy 的提交/推送范围必须由当前任务授权决定；计划文字不自动授权 push、PR、merge 或远端设置变更。
- 测试治理不自动授权部署、发布、生产访问或生产数据操作。

## 3. 文档阅读与规则权威

### 3.1 默认必读

每批只读取与当前任务直接相关的现行材料，边界固定如下：

| 层级 | 是否读取 | 范围 |
|---|---|---|
| 工作区入口 | 强制 | 当前 `AGENTS.md`、`docs/总规则.md` |
| 当前项目规则 | 强制 | 本批所属项目的一份规则入口；AI 另读其项目 `AGENTS.md` 和业务规则 |
| 当前专题契约/红线 | 命中模块时强制 | 只读与改动模块、调用链和风险直接相关的架构契约、缓存/权限/启动/部署等专题红线 |
| 当前源码与测试 | 强制 | 改动目标、直接调用方/被调用方、对应测试与必要的项目图；不展开无关模块 |
| 总计划 | 执行治理任务时强制 | 本文件的总目标、当前项目矩阵、当前 Batch 和完成定义；普通窄任务不全文加载其他项目章节 |
| 近期 Git/GitHub | 需要冻结行为或确认回归来源时读取 | 限相关文件、相关 commit/PR/check，不做无边界历史漫游 |
| 滚动复盘、历史核心记录、旧计划、运行日志、历史证据 | 默认不读 | 只有 3.2 的触发条件命中后才定向检索，不得默认全文加载 |

“强制读取”不等于把整个文档树全部载入；强制的是当前权威入口和命中模块的红线。任何 Agent 不得以“可能有用”为理由全文读取三个项目的复盘、旧计划或历史证据。

### 3.2 历史材料按需检索

以下材料默认封存、按需检索：

- 三个项目的滚动复盘；
- `docs/历史核心记录.md`；
- 旧计划、运行日志、已退役治理偏航研究、临时候选和取证流水；
- 已被正式规则替代的阶段报告。

出现以下情况时，必须按模块名、Rule ID、错误码、关键类型或故障症状检索相关历史：

- 修复历史回归；
- 修改已冻结业务链路；
- 当前实现与专题契约冲突；
- 测试失败原因无法从源码和契约确定；
- 同类问题曾经发生；
- 用户明确要求追溯历史决策。

### 3.3 规则不能只存在于复盘

- 当前有效约束必须存在于总规则、项目规则或专题契约。
- 可检测约束必须进入测试、Analyzer、MSBuild/static gate 或 CI。
- 复盘保留改动经过、验证命令、现场故障和决策原因；已提炼规则只保留 Rule ID 和权威链接。
- 规则提取审计证据已形成后，不再把全文复盘阅读作为测试工作前置条件。

每次复盘结尾固定使用以下二选一模板：

```markdown
长期规则结论：

- 无新增长期规则；原因：<本批仅实现既有规则或修复实现偏差>。
```

```markdown
长期规则结论：

- 已沉淀为 `<Rule ID>`：<一句话规则>
- 权威位置：<正式规则或专题契约链接>
- 自动门禁：<测试、Analyzer、policy gate 或 CI>
- 人工验收/治理债务：<无则写“不适用”>
```

最终关系固定为：

```text
专题契约：现在必须遵守什么
自动化测试：违反后如何阻断
历史复盘：当时为什么这样决定
```

### 3.4 上下文与交接门禁

- 每批开始记录“已读强制材料、命中的专题红线、按需检索词和未读历史材料”，主 Agent 终审时复核范围是否足够且没有无关扩张。
- 历史检索先用模块名、Rule ID、错误码、类型名或症状执行 `rg`，只打开命中附近和必要关联条目；一次命中不能自动授权继续全文阅读整个复盘。
- 历史结论与当前规则冲突时，以当前总规则、项目规则和专题契约为准，并修正仍在活动入口中的过期表述。
- Agent 交接只传当前目标、硬边界、相关事实、未完成项和验证证据，不复制大段无关历史，避免上下文再次膨胀。

## 4. 统一测试分类模型

### 4.1 主分类 TestKind

每个测试只能有一个主要 `TestKind`。主要分类决定物理归口、允许依赖和 CI job。

| TestKind | 主要责任 | 关键边界 |
|---|---|---|
| `Architecture` | 项目图、分层、DDD、插件、禁止引用、结构契约 | 不承载业务 happy path |
| `Unit` | parser、formatter、policy、validator、纯算法 | 禁止真实 DB、网络、容器、UI runtime |
| `Aggregate` | 聚合不变量、状态转换、child、领域事件 | 只依赖 Domain/Core |
| `Application` | Command/Query、权限、业务用例、端口调用 | 使用受控 fake port，不直连 provider |
| `Contract` | HTTP/DTO/Event/SSE/PLC/MES/Cloud 协议兼容 | 验证公开边界，不复制生产 parser |
| `Conformance` | 插件、模块、provider 对统一规范的符合性 | 与模块内部完整 workflow 分开 |
| `Persistence` | mapping、migration、transaction、durable state、Outbox | 使用真实关系语义或受控 SQLite/Postgres |
| `Workflow` | 分支、重试、取消、超时、补偿、幂等、死信 | 使用可控 clock/barrier，不靠 sleep |
| `Integration` | DI、Host、middleware、auth、Redis/RabbitMQ/HTTP/filesystem adapter | 不冒充完整用户 E2E |
| `EndToEnd` | 用户入口到关键真实依赖的少量闭环 | 控制数量，不重复底层矩阵 |
| `UI` | mount/headless、交互、焦点、语言、窗口、可访问性 | 禁止读源码字符串冒充 UI 行为 |
| `GoldenEval` | 版本化输入输出、安全矩阵、AI 质量基线 | 必须执行真实生产路径 |
| `Deployment` | 脚本行为、包布局、hash、升级、回滚 sandbox | 不执行未授权生产发布 |
| `Performance/SoakChaos` | 性能、长稳和故障注入 | Nightly/Release/Manual，不进普通 Unit |

Security 通常使用 `Concern=Security`，并保留真实主要 TestKind。Regression 是 `RegressionId`，不是新的测试项目或大桶。

### 4.2 横向维度

每个测试资产至少记录：

- `Capability`：业务归属；
- `Runtime`：Pure、InProcess、Filesystem、SQLite、Postgres、Redis、RabbitMQ、Docker、Aspire、Avalonia、Browser、Windows、LiveExternal；
- `Risk`：P0、P1、P2；
- `Concern`：Security、Reliability、Compatibility、Accessibility、Performance；
- `Cadence`：PR、Nightly、Release、Manual；
- `Profile`：Default、Simulation、GoldenDataset、LiveExternal；
- `RegressionId`、`RuleId`、`Owner`。

禁止继续用 `Phase*`、`Batch*`、年份、`Misc`、`General`、`Backend`、`NonUi` 或大而含混的 `Regression` 作为新增测试主分类。

### 4.3 物理拆分原则

满足任一条件才拆独立测试项目：

- 允许的生产依赖不同；
- 运行时或凭据边界不同；
- 平台不同；
- CI 频率、超时或 runner 不同；
- 必须通过 csproj 依赖证明边界。

否则先用目录、namespace 和 metadata 归位，避免为了计划表一次性创建大量空项目。

### 4.4 TestKit

- 只使用 `*.Testing` 或 `*.TestKit` 命名，不建立 `CommonTests`。
- TestKit 不得包含测试 case。
- Pure 与 Integration TestKit 物理分离。
- 一个测试类专用 helper 保持 local/private。
- 不得复制生产 parser、serializer、sanitizer、hash、SQL guard 或协议 codec。
- 生产项目引用 Tests/TestKit 必须在 build 阶段失败。
- 跨三仓只共享规范和 fixture 格式，不共享业务测试代码包。

## 5. 必须建立的测试门禁

### 5.1 Inventory、数量和 Skip

每个项目生成机器可读 inventory，至少包含 assembly、测试类/case、TestKind、Runtime、Risk、Cadence、Owner、是否 required、是否 Skip 和最近状态。

迁移批次必须满足：

- `discovered = executed`；
- `failed = 0`；
- `skipped = 0`；
- 测试总数不得无说明下降；删除重复/无效 case 时必须列出替代覆盖；
- 同一个 RegressionId 不得因迁移失去全部覆盖；
- 旧入口迁空后物理删除，禁止长期双跑。

### 5.2 两层架构门禁

第一层是编译型静态门禁：

- .NET 使用 Roslyn Analyzer、MSBuild project graph、程序集可见性和 `.editorconfig` error。
- Vue/TypeScript 使用 type-check、lint、依赖图和语义 static gate。
- 每条规则有稳定 Rule ID、原因、修复建议和精确例外。
- AnalyzerTests 覆盖正例、反例、别名、泛型、helper 包装、跨文件和无误报场景。
- 不用 `File.ReadAllText + Contains/Regex` 替代可用的符号或项目图分析。

第二层是动态 Architecture/Integration gate：

- 验证 DI、EF model、migration ownership、HTTP metadata、manifest、真实装载、关系数据库和运行时组合事实。
- 在业务测试前运行；失败时阻断后续结论。
- 输出 Rule ID、实际依赖路径和最短修复建议。
- 不把 `dotnet test` 递归挂入 build 来伪造编译错误。

工作区规则模板继续使用 `WSARCH*`、`DDD*`、`DATA*`、`PLUG*`；三仓实现时使用各仓稳定诊断前缀，避免跨仓诊断号混淆。

### 5.3 CLOUD-CACHE-001

长期规则固定为：

> 缓存故障可以受控降级；调用方取消和 factory 业务异常必须传播，降级不得导致 factory 重复执行。

实施前盘点 `ICacheService` 全部调用点，区分普通值缓存与权限、租约、幂等、Outbox 等安全敏感用途。普通缓存的受控降级不能覆盖安全链路应有的 fail-closed 语义。

Required 最小矩阵：

| 场景 | 预期 | factory 次数 |
|---|---|---:|
| `GetAsync/SetAsync/RemoveAsync/RemoveByPatternAsync` 调用方取消 | 传播 `OperationCanceledException`，不记成缓存故障 | 不适用 |
| `GetOrSetAsync` 入口已取消 | 原样传播取消 | 0 |
| 缓存访问期间取消且 factory 未开始 | 原样传播取消 | 0 |
| factory 执行期间取消 | 原样传播取消 | 1 |
| factory 完成后写回阶段取消 | 传播取消，不重试 factory | 1 |
| factory 抛普通业务/数据库异常 | 传播同一异常实例 | 1 |
| factory 抛看似缓存异常的异常 | 仍按 factory 来源原样传播 | 1 |
| 批准的缓存基础设施读取故障 | 按契约降级为 miss/default 并记录 warning | 不适用 |
| 缓存故障后 fallback factory 成功 | 返回 factory 结果 | 1 |
| 缓存故障后 fallback factory 失败 | factory 异常优先，缓存异常不得覆盖 | 1 |
| 非 allowlist 或来源不明异常 | 传播，不得降级 | 至多 1 |
| 权限/设备访问等安全敏感缓存故障 | 禁止返回 stale 授权，按专题安全契约直读或 fail-closed | 至多 1 |

还必须分别覆盖 `SetAsync(value: null)` 委托删除，以及 `RemoveByPatternAsync` 的 endpoint 枚举、key scan、逐 key remove 取消点。确定性测试和受控真实 Redis 故障测试都进入 Cloud required CI。

### 5.4 TEST-EXC-001 异常与取消

三个项目统一遵守：

1. 调用方取消必须传播 `OperationCanceledException`，不得翻译成成功、默认值、空集合或普通业务失败。
2. Domain/Application/factory 的业务异常必须保留原异常实例，或只在已声明的 HTTP/UI 边界映射为稳定错误码。
3. 只有 adapter 边界可以按明确 allowlist 翻译基础设施异常；未知异常继续传播。
4. 禁止 `catch (Exception)` 后返回成功、空值、空集合或伪造健康状态。
5. retry、fallback、cache miss 和错误映射不得重复执行 factory、数据库写入、消息发布、MES/Cloud 上传或其他副作用。
6. 基础设施故障与业务异常同时发生时，最终错误必须按专题契约确定优先级，不能让降级覆盖业务异常。

每个被治理入口至少覆盖取消前、await 期间取消、业务异常、批准基础设施异常、未知异常和副作用次数。

### 5.5 Flaky 与并发

- `EDGE-PLUGIN-TEST-SEAM-001` 把现有具体工序插件中的并发/半完成状态问题还原为通用插件生命周期与测试完成边界：宿主/SDK 使用中性 TestPlugin fixture 验证 capture、enqueue、completion、callback 和取消顺序；具体工序业务断言不再代表 Edge 总体门禁。
- 现有具体工序 flaky 只作为迁移证据。若问题属于通用 SDK 契约，则迁入中性 fixture 后修复；若只属于某个工序业务，则留给对应插件仓及其业务文档，不升级为宿主主线任务。
- 使用 `TaskCompletionSource`、barrier、可观察 completion、可控 clock 或 fake transport。
- 禁止固定 sleep、无条件重跑、Skip、放宽断言或扩大超时换绿。
- 首次偶发失败必须保存 seed、runner、日志和 artifact；重跑只能用于诊断，不能覆盖首次失败事实。
- 无法隔离的共享外部资源按 collection/namespace/database 串行；纯测试默认并行。

### 5.6 测试编写标准

- 一个测试验证一个主要失效机制。
- Arrange 只准备必要状态；Act 只有一个主要行为；Assert 针对可观察结果。
- 下层断言业务状态，上层断言 transport、组合和用户可见结果，不复制下层全部矩阵。
- 失败断言稳定 error code/state/ProblemDetails，不把日志文字作为唯一证据。
- 时间、ID、随机数和执行顺序可控。
- UI 使用真实 mount/headless/用户交互；Golden 必须调用生产路径。
- 缺 Docker/Aspire/Browser 时，job 必须失败或不调度；测试运行后不得静默 Skip。

## 6. 三项目目标测试架构

下表表达责任层，不要求一次性创建所有 csproj。先归类，再按依赖/运行时差异物理拆分。

### 6.1 EdgeClient

当前 solution 中的三个模切项目属于待物理退役资产，不进入目标架构，也不能作为 Analyzer 的合法插件/共享工程正例。目标测试架构只针对退役后仍存在的宿主、基础设施和真实插件能力。

| 层 | 主要内容 |
|---|---|
| Architecture/Analyzer | layer、项目图、DDD、DB owner、插件、PLC owner、禁止 API |
| Domain/Application | 已裁决 AggregateRoot、Value Object、用例、权限、端口交互 |
| Contract | PLC frame、MES payload、Cloud paths/DeviceId、Update catalog/hash |
| Conformance | module manifest、identity、capability、ViewId、装载和包内容 |
| Persistence/Workflow | SQLite、migration、PLC lifecycle、DataPipeline、Cloud/MES 双出口、retry/deadletter |
| Startup | 缺配置、PLC/MES/Cloud 不可达、IO/profile 失败仍进入 Shell |
| UI | Shell、Launcher、Installer、UI.Shared 的 Unit/Headless/Automation |
| Deployment/Windows | 包布局、Velopack、安装、升级、ProgramData 和下载 artifact |

现有大桶迁移：

- `NonUiRegressionTests` 按真实失效机制迁入 Domain/Application/Contract/Persistence/Workflow/UI，迁空后删除。
- `RepositoryHygieneTests` 的语义规则迁 Analyzer，动态事实迁 Architecture/Deployment/UI。
- `Module.ContractTests` 只保留 Conformance；协议、Persistence 和 Workflow 移出。
- Shell、Launcher、Installer 按 Unit/UI/Startup/Windows runtime 分开。

Edge 顺序：`EDGE-GOV-PURGE-001 → EDGE-DIECUT-REMOVE-001 → EDGE-CI-REBASELINE-001 → EDGE-LOCAL-PURGE-001 → EDGE-PLUGIN-TEST-SEAM-001 → EDGE-ARCH-001 → Domain/Application → Contract/Conformance → Persistence/Workflow → Startup/UI/Windows → cleanup`。

宿主仓/插件仓拆分只作为下一份独立计划的交接备注：当前不创建仓库、不移动代码、不修改 remote，也不把拆仓作为 flake、Analyzer 或测试分层的前置条件。

### 6.2 CloudPlatform

| 层 | 主要内容 |
|---|---|
| Architecture/Analyzer | layer、DDD、DB access、AiRead 只读、授权 metadata、前端边界 |
| Domain/Application | Identity、Employee、MasterData、Production 聚合与用例 |
| Contract | HTTP/OpenAPI/Event/OIDC、Edge provider、AI read-only consumer |
| Integration | Postgres、Redis、RabbitMQ、Filesystem、真实 HTTP/DI/auth |
| Workflow | ClientRelease、Outbox、worker、ACK-lost、retention、rollback |
| Frontend | type-check、unit、component、contract、smoke、少量 E2E |
| Deployment | policy、impact、权限、rollback sandbox |

现有 `ServiceLayer.Tests`、`ProductionService.Tests` 和混合 `EndToEndTests` 不再吸收新职责；先冻结，后按真实失效机制迁移。Cloud 首项是 `CLOUD-CACHE-001`，然后进入 `CLOUD-ARCH-001` 和物理分层。

### 6.3 AICopilot

| 层 | 主要内容 |
|---|---|
| Architecture/Analyzer | layer、DDD、DB、identity、plugin、Cloud read-only |
| Unit/Aggregate/Application | parser、policy、token budget、聚合、权限和业务用例 |
| Workflow | Agent branch、approval、cancel、timeout、failure、compensation |
| Contract/Conformance | OpenAPI、SSE、ProblemDetails、Tool/Plugin manifest/capability |
| Persistence/HTTP | Postgres、Outbox、file marker、真实 auth/middleware/tracking |
| Eval/E2E | deterministic Golden/Eval、Simulation、少量真实 Aspire 关键链 |
| LiveExternal | CloudAiRead 与真实模型/provider，仅 Manual/Release 非生产环境 |

`AICopilot.BackendTests` 按上述职责迁空后删除；`Phase*`/`Batch*` filter 退役；纯测试恢复并行。

`AI-SEC-051-TEST` 作为首个分层样板，分别验证 policy、Persistence 锁与重试、真实 HTTP tracking/auth、稳定错误契约、rejected audit 持久化和 Architecture owner，不复制同一断言。

## 7. CI 与生产 CI/CD 边界

### 7.1 本计划负责的 CI

普通 CI 负责：

- build、Analyzer 和 project graph；
- 受影响分层测试；
- inventory、发现/执行/Skip 对账；
- 缓存、异常、取消、权限和补偿高风险矩阵；
- duplication delta、secret 和依赖漏洞；
- 独立的 deployment policy/impact sandbox 测试；
- 稳定结果汇总和 artifact。

CI 实施原则：

- GitHub CI 不是唯一真相，同一套核心命令必须能在本机或对应 Windows runner 上复现。
- 使用远端 CI 的批次必须检查适用 job、annotations 和 artifact；纯本机/文档批次不伪造远端证据。
- baseline 和数量用于防止静默删测，不得阻止经明确裁决的功能退役；删除功能后按真实剩余资产建立新基线。
- CI 只运行独立的测试、Analyzer、构建、打包静态检查和影响分析测试，不调用生产发布入口。

### 7.2 PR、Nightly、Release、Manual

| 层级 | 内容 |
|---|---|
| PR | build、Analyzer、Architecture、Unit/Aggregate/Application、受影响 Contract/Integration、0 Skip、duplication delta |
| Nightly | 全量 DB/Redis/RabbitMQ/SQLite、并发/chaos、mutation、性能、visual、扩展 Eval |
| Release | Edge Windows 安装/升级；Cloud/AI migration、artifact、rollback；三仓非生产契约对齐 |
| Manual/LiveExternal | 真实 PLC/MES/Cloud/模型、现场设备、类生产故障演练和生产操作 |

PR required checks 总墙钟 p95 目标为 25 分钟。超预算时优化 artifact 复用、fixture、并行和 impact mapping；禁止删测试、Skip、过滤或放宽断言换绿。

Release 测试通过不等于自动授权部署。

### 7.3 生产交付/部署边界

工作区现有 CI/CD 总体入口与生产发布语义不是本计划的改造对象。本计划不运行 `deploy/Deploy-Changed.ps1`，也不使用其 `-PlanOnly` 模式冒充离线测试；required CI 直接执行独立 policy/impact 测试。

删除功能时必须同步从 solution、构建图、bundle/catalog、影响分析、preflight、打包和发布清单中删除对应条目，使现有 CI/CD 不再识别已退役功能。这是物理退役的一部分，不是保留兼容，也不是新增发布链。

下一份拆仓计划需要单独设计双仓 CI 触发、artifact contract 和增量影响映射；当前计划不提前修改。

## 8. 重复代码、兼容代码、Coverage 与 Mutation

### 8.1 Duplication

分别统计：

- 生产代码 exact/near clone；
- 测试 fixture/harness/helper clone；
- 测试 case 断言流程 clone；
- 跨仓 clone只报告，不自动抽共享包。

首次只建立 baseline，之后：

- PR 不得新增 exact clone group；
- 现有 clone 的行数、token 和实例数不得扩大；
- 收口 clone 后同步降低 baseline，禁止反弹；
- TestDoubles、fake server、mirror DTO、serializer、hash、SQL guard 和重复 Handler 测试优先治理；
- 不因相似度强行合并不同 bounded context、工序或 Cloud/MES 语义。

### 8.2 兼容代码治理

建立 `TEST-COMPAT-001` 三仓清单。兼容代码包括旧 endpoint/DTO、旧模块或 profile alias、adapter、wrapper、fallback、双写/双读、影子注册、已退役 feature flag 和只为历史名称存在的测试。

- 每一项必须记录 producer、当前 consumer、调用证据、替代路径、删除条件、最晚删除批次和覆盖测试；没有真实 consumer 的项直接删除，不为“也许以后”保留。
- 仍有真实外部 consumer 的项只能作为有期限迁移窗口，CI 同时验证旧入口与新入口的一致性，并用计数 ratchet 禁止新增调用方。
- 到期后同步删除生产代码、DI/路由/配置、文档、fixture、测试和发布输入；禁止空 wrapper、永久 fallback、双写常驻或只把旧代码移动到 `Legacy/Compat` 目录。
- 兼容治理不得借机改变未授权的业务协议；无法证明 consumer 状态时先做只读 inventory 和契约测试，再由项目批次裁决。

### 8.3 Coverage 与 Mutation

- Coverage 用于发现盲区，不是单一完成定义。
- 既有代码先建立 no-regression baseline。
- 新增 P0 Domain/Application 建议 line ≥ 90%、branch ≥ 85%；普通新增业务代码建议 line ≥ 80%、branch ≥ 70%，在两轮 CI 数据后校准。
- Mutation 优先用于 Aggregate 不变量、权限 policy、状态机、重试分类和安全 guard。
- 第一阶段只报告 mutation baseline；稳定后对 P0 新代码实施不下降 ratchet。

## 9. 分阶段路线

### Phase A：错误治理资产退役

- 按仓执行 `TEST-GOV-RETIRE-001`，物理删除平行授权链、额外审批门禁及其 policy/fixture/活动文档引用。
- 不保留 optional、compat、fallback 或空壳入口；事故经过只留在指定复盘和 Git 历史。
- 保留真正有用的 inventory、build、真实 discovery、0 Skip 和完整 diff 验证。

### Phase B：Edge 模切退役与重新对账

- 执行 `EDGE-DIECUT-REMOVE-001`，删除三个模切项目及源码、UI、配置、测试、solution、打包和发布输入清单。
- 执行 `EDGE-LOCAL-PURGE-001`，清空工作区可再生成的 `bin/obj/.artifacts/publish` 和测试 staging，不碰服务器、Cloud release history 或现场安装。
- 执行 `EDGE-CI-REBASELINE-001`，按真实剩余项目、测试和 runner 建立新基线；删除功能对应的专属测试允许有说明地减少。

### Phase C：Inventory、旧桶冻结和现有 CI 补齐

- 三仓 inventory 唯一分类、发现/执行/Skip 对账。
- 冻结 Edge NonUI/RepositoryHygiene、Cloud 混合 E2E、AI BackendTests 的新增职责。
- 现有漏跑测试接入 CI。
- Edge 把具体工序插件测试与宿主/SDK 通用插件契约分开，宿主使用中性 TestPlugin fixture。

### Phase D：高风险语义门禁

可并行：

- Edge：`EDGE-PLUGIN-TEST-SEAM-001`，收口通用插件生命周期、完成和取消边界。
- Cloud：`CLOUD-CACHE-001`。
- AICopilot：`AI-SEC-051-TEST` 的红测和分层基线。
- 三仓：`TEST-EXC-001` 取消、异常与副作用次数矩阵。

### Phase E：三仓 Analyzer + AnalyzerTests

- Edge：`EDGE-ARCH-001`。
- Cloud：`CLOUD-ARCH-001`。
- AI：`AI-ARCH-001`。
- 每仓独立实现诊断，不发布跨三仓万能 Analyzer 包。

### Phase F：Unit/Aggregate/Application 归位

- 先迁最纯、最快、可并行的核心测试。
- 收紧 ProjectReference；Pure 测试不间接引用 Integration TestKit。
- 补齐聚合不变量、权限、取消、幂等和副作用。

### Phase G：Contract/Conformance/Persistence/Workflow

- Edge：PLC/MES/Cloud/Update、Module、SQLite、DataPipeline。
- Cloud：HTTP/Event/OIDC、Postgres/Redis/RabbitMQ/File、Outbox/ClientRelease。
- AI：SSE/OpenAPI、Tool/Plugin、Postgres/File、Agent workflow。
- 使用 barrier/interceptor/fault injection 替代固定 delay。

### Phase H：HTTP/Startup/UI/E2E/Golden

- Edge 完成非阻断启动矩阵、Headless 和 Windows Release lane。
- Cloud 收口真实 HTTP/前端组件/关键 E2E。
- AI 完成真实 HTTP auth/tracking 和 production-path Eval。
- E2E 数量受控，只验证下层不能覆盖的组合失效。

### Phase I：质量 ratchet 与最终清理

- duplication、compatibility inventory、coverage、mutation、性能和 flaky 指标稳定。
- 跨仓 contract digest 按 Provider → Consumer → 非生产联合验收串行。
- 旧大桶、旧 filter、重复 TestKit、过期兼容层和无效源码字符串门禁迁空后物理删除。

### 本计划之外的下一计划交接：Edge 宿主仓与插件仓拆分

- 这里只交接用户已经确定的方向，不定义当前 Batch ID、入口条件、详细架构、仓库数量或 CI/CD 方案。
- 下一份独立计划再裁决 SDK/API、UI、manifest、版本、artifact 所有权、仓库粒度和双仓 CI/CD。
- 当前计划不创建仓库、不移动源码、不修改 remote，不把拆仓计入进度，也不让它阻塞三项目测试单元架构。

## 10. 批次清单

### 10.1 工作区

| ID | 内容 | 状态 |
|---|---|---|
| `TEST-GOV-RETIRE-001` | 三仓逐仓物理退役错误治理资产和活动引用 | 已完成 |
| `TEST-GOV-001` | inventory、分类 schema、旧桶冻结 | 已完成；三仓声明、发现、执行和 runner 清单已对账 |
| `TEST-GOV-002` | Aggregate/plugin/persistence/outbound owner 裁决 | 已完成；测试职责和物理项目已归位 |
| `TEST-GOV-003` | Rule catalog、project graph、架构门禁 | 已完成；三仓 Analyzer、AnalyzerTests 和静态违规 fixture 已接入 required CI |
| `TEST-GOV-004` | discovered/executed/Skip/flaky 对账 | 已完成；三仓 required 均为 failed 0、skipped 0 |
| `TEST-GOV-005` | 跨仓 contract digest | 已完成；`edge-pass-station-batch-v1` digest 与非生产联合验收已固化 |
| `TEST-GOV-006` | duplication baseline/ratchet | 已完成；仓内 ratchet 与跨仓 report-only 基线已形成 |
| `TEST-COMPAT-001` | 三仓兼容代码 inventory、consumer 证明、到期删除和增量 ratchet | 已完成；无 consumer 项已删除，保留项已分类并受增量门禁约束 |
| `TEST-GOV-007` | coverage/mutation baseline | 已完成；三仓 baseline 生效，AI final mutation 为 58/58 killed |
| `TEST-GOV-008` | 25 分钟 p95 与最终汇总 | 已完成；Edge 21m23（n=2）、Cloud 12m19（n=1）、AI 23m02（n=3），样本限制已显式记录 |
| `TEST-GOV-RULE-EXTRACTION-001` | 历史规则提取和按需阅读 | 已完成；当前规则已沉淀至正式规则/专题契约 |
| `TEST-EXC-001` | 三仓异常、取消和副作用语义 | 已完成；高风险语义测试与门禁已进入 required CI |

### 10.2 Edge

| ID | 内容 | 状态 |
|---|---|---|
| `EDGE-GOV-PURGE-001` | 删除额外审批文件、平行授权工具及 policy/fixture/文档引用 | 已完成 |
| `EDGE-DIECUT-REMOVE-001` | 物理删除三个模切项目和全部活动/本地残留 | 已完成 |
| `EDGE-CI-REBASELINE-001` | 按退役后的真实 solution、声明、execution、runner 和 0 Skip 重建基线 | 已完成；32 runner、1280/1280 |
| `EDGE-LOCAL-PURGE-001` | 清空本机可再生成的 bin/obj/.artifacts/publish/staging | 已完成；最终生成目录为零 |
| `EDGE-PLUGIN-TEST-SEAM-001` | 中性 TestPlugin 覆盖通用生命周期/完成/取消；具体工序测试归插件 | 已完成 |
| `EDGE-ARCH-001` | layer/DDD/DB/plugin/async Analyzer + AnalyzerTests | 已完成 |
| `EDGE-TEST-GOV-002` | Domain/Aggregate/Application | 已完成 |
| `EDGE-TEST-GOV-003` | PLC/MES/Cloud Contract + Module Conformance | 已完成 |
| `EDGE-TEST-GOV-004` | Persistence/Workflow/确定性并发 | 已完成 |
| `EDGE-STARTUP-TEST-001` | 非阻断启动矩阵 | 已完成 |
| `EDGE-UI-TEST-001` | Shell/Launcher/Installer/UI.Shared | 已完成自动化边界；Windows 实机仍按计划排除并明确记录 |
| `EDGE-QUALITY-001` | duplication/coverage/mutation | 已完成 |
| `EDGE-COMPAT-001` | 过期 alias/adapter/wrapper/fallback/影子路径清理 | 已完成 |
| `EDGE-TEST-CLEANUP` | 删除旧大桶和重复 helper | 已完成 |

### 10.3 Cloud

| ID | 内容 | 状态 |
|---|---|---|
| `CLOUD-TEST-001` | 后端、Vitest、deployment behavior 现有门禁 | 已完成；required 661/661，Web 69，deployment 33 |
| `CLOUD-CACHE-001` | 缓存、取消、factory 和安全缓存语义 | 已完成 |
| `CLOUD-ARCH-001` | layer/DDD/DB/AiRead/auth Analyzer + AnalyzerTests | 已完成 |
| `CLOUD-TEST-002` | Domain/Aggregate/Application | 已完成 |
| `CLOUD-TEST-003` | HTTP/OpenAPI/Event/Edge/AI/OIDC Contract | 已完成 |
| `CLOUD-TEST-004` | Postgres/Redis/RabbitMQ/File Integration | 已完成 |
| `CLOUD-TEST-005` | ClientRelease/Outbox/Worker Workflow | 已完成 |
| `CLOUD-FRONTEND-001` | type-check/unit/component/contract/smoke/E2E | 已完成 |
| `CLOUD-QUALITY-001` | duplication/coverage/mutation | 已完成 |
| `CLOUD-COMPAT-001` | 过期 endpoint/DTO/cache adapter/fallback 清理 | 已完成 |
| `CLOUD-TEST-CLEANUP` | 删除旧大桶和重复 fixture | 已完成 |

### 10.4 AICopilot

| ID | 内容 | 状态 |
|---|---|---|
| `AI-TEST-001` | Backend/Vitest/deployment inventory 和冻结 | 已完成；25 项目、1025 cases，required 1011/1011 |
| `AI-SEC-051-TEST` | 安全不变量分层样板 | 已完成 |
| `AI-ARCH-001` | layer/DDD/DB/identity/plugin/read-only Analyzer | 已完成 |
| `AI-TEST-002` | Unit/Aggregate/Application 并恢复并行 | 已完成；四 lane 并行与 always-run 汇总已稳定 |
| `AI-TEST-003` | Agent Workflow/Simulation | 已完成 |
| `AI-TEST-004` | Contract/Persistence/HttpIntegration | 已完成 |
| `AI-EVAL-001` | production-path Golden/Eval | 已完成自动化边界 |
| `AI-FRONTEND-001` | unit/component/contract smoke/E2E | 已完成；Vitest 165/165、Playwright 43/43 |
| `AI-QUALITY-001` | duplication/coverage/mutation | 已完成；final mutation 58/58 killed，score 100% |
| `AI-COMPAT-001` | 过期 Tool/Plugin/API/workflow adapter/fallback 清理 | 已完成 |
| `AI-TEST-CLEANUP` | 删除 BackendTests 和历史 filter | 已完成 |

## 11. 多 Agent 执行协议

多 Agent 适合本计划，但并行的是独立项目批次，不是同一文件或同一提交的零散步骤。

1. 主 Agent 登记 Project、Batch ID、base SHA、允许/禁止路径、worker、worktree、资源前缀和验证矩阵。
2. 用户授权多个项目写入后，Edge、Cloud、AI 可以各由一个写 Agent 并行推进。
3. 同一项目同一时间默认只有一个写 Agent；不能共享 branch、worktree、测试输出、数据库、Redis namespace、Docker network、端口或数据根。
4. 工作区共享文档只由主 Agent 修改，项目 Agent 不顺手改总规则或总计划。
5. 项目 Agent 交付候选、完整 diff、验证命令、发现/执行/Skip、artifact、未验证项和最终 status。
6. 主 Agent 必须直接复核完整 diff、未跟踪文件、`git diff --check`、数量对账和高风险测试，不只采信摘要。
7. Provider → Consumer 跨仓契约升级串行；使用同一现场设备、Windows runner、固定端口或生产资源的测试串行。
8. 多 Agent 可以并行开发和验证；生产发布始终另行授权并由主 Agent 串行发起。

推荐并行波次：

```text
Edge 清理与 rebaseline ──> Edge 通用插件 seam ─┐
Cloud cache ────────────────────────────────────┼─> 主 Agent 终审 ─> 三仓 Analyzer 并行 ─> 分层迁移
AI-SEC-051 红测 ────────────────────────────────┘
```

## 12. 每批固定执行协议

1. 读取当前规则、相关架构契约、源码、测试和近期历史；历史复盘只在命中第 3.2 节条件时定向检索。
2. 执行 `git status`、近期 `git log`，记录 base SHA 和既有脏改动。
3. 输出当前 inventory、目标 TestKind、允许路径、排除项和验证矩阵。
4. 先建立红测、违规 fixture 或当前漏跑证据。
5. 测试迁移与生产语义修复分批；确需同批时必须证明它们是同一失效闭环。
6. 运行 build、Analyzer、责任测试、inventory reconciliation 和适用 Integration。
7. 检查 failed、skipped、flaky、fixed delay、重复 helper、secret 和未跟踪文件。
8. 更新项目滚动复盘；形成长期规则时同步正式规则/专题契约。
9. 只有当前任务明确授权时才 commit/push/创建 PR；普通 push 优先。
10. 远端 CI 属于本批范围时读取 jobs、annotations 和 artifact；不靠重跑掩盖首次失败。
11. 部署、LiveExternal、真实 PLC/MES/Cloud/模型和生产数据另行授权。

## 13. 完成定义

总计划只有同时满足以下条件才可关单：

- 错误治理模型的可执行资产、审批门禁、policy/fixture 和活动文档引用已从各仓物理删除；只允许指定复盘和 Git 历史保留事故事实。
- Edge 模切源码、UI、配置、测试、solution、打包/发布输入和本地产物为零；没有空工程、stub、alias 或兼容入口。
- Edge 宿主/SDK 的插件测试使用通用契约和中性 fixture，不把任何未定稿具体工序当成宿主架构前置。
- 三项目所有测试资产唯一分类，无未决文件和新增旧大桶。
- 旧混合测试项目迁空并物理删除，旧 CI filter 和重复 helper 零引用。
- Required `discovered = executed`、`failed = 0`、`skipped = 0`。
- 三仓静态架构违规 fixture 能稳定使 build/static gate 失败，AnalyzerTests 无误报回归齐全。
- Edge startup、Cloud cache、三仓异常/取消、权限、补偿和副作用次数门禁生效。
- Contract、Conformance、Persistence、Workflow、HTTP/UI/E2E/Golden 各归真实失效层。
- 生产和测试 duplication ratchet 生效；coverage/mutation baseline 稳定。
- 纯测试默认并行，PR p95 达到或接近 25 分钟且无靠删测换绿。
- 跨仓 contract digest 和非生产联合验收入口建立。
- 当前规则存在于正式契约；历史复盘不再是唯一规则来源。
- 未执行的 Windows 实机、现场、生产和 LiveExternal 验收明确记录，未被冒充完成。

### 13.1 2026-07-16 历史登记矩阵

截至 2026-07-16，本计划曾按当时证据登记为 **14/14 全部满足**。该表仅保留当时的登记事实；2026-07-17 后续客观审核和行级代码复核发现完成谓词未封闭、证据绑定不完整以及真实漏跑测试，因此它不再代表当前终态：

| # | AND 完成条件 | 最终证据 | 结论 |
|---:|---|---|---|
| 1 | 错误治理资产物理退役 | 三仓治理文件、policy/fixture、活动引用和兼容入口已删除；required CI 全绿 | 满足 |
| 2 | Edge 模切与本地产物归零 | 三个模切项目及活动输入已物理删除；最终清理后 `.vs/bin/obj/artifacts/.artifacts/publish/staging` 为零 | 满足 |
| 3 | Edge 中性插件测试 seam | 宿主/SDK 使用中性 TestPlugin 和通用生命周期/完成/取消契约 | 满足 |
| 4 | 三仓测试资产唯一分类 | Edge 32 runner；Cloud inventory 18/725；AI 25 项目/1025 cases，均无未决分类 | 满足 |
| 5 | 旧大桶/filter/helper 物理清理 | 旧混合桶、历史 filter 和重复 helper 已迁空或受零新增门禁约束 | 满足 |
| 6 | Required 对账全绿 | Edge 1280/1280；Cloud 661/661；AI 1011/1011，三仓均 failed 0、skipped 0 | 满足 |
| 7 | Analyzer 与违规 fixture | 三仓 Analyzer、AnalyzerTests、project graph/static gate 均进入 required CI | 满足 |
| 8 | 高风险语义门禁 | Edge startup/插件/PLC-MES-Cloud 分离、Cloud cache、AI auth/tracking/read-only 及三仓异常/取消/副作用均有自动化门禁 | 满足 |
| 9 | 测试归真实失效层 | Contract、Conformance、Persistence、Workflow、HTTP/UI/E2E/Golden 已按各仓职责落位 | 满足 |
| 10 | duplication/coverage/mutation 稳定 | 仓内 ratchet 已生效；[跨仓 duplication 基线](../artifacts/testing/cross-repository-duplication/cross-repository-duplication.json)为 report-only；AI mutation 58/58 killed | 满足 |
| 11 | 并行与 25 分钟目标 | Edge 21m23（n=2）、Cloud 12m19（n=1）、AI 23m02（n=3）；未通过删测、Skip 或放宽断言换绿 | 满足 |
| 12 | digest 与非生产联合验收 | [contract digest](../artifacts/testing/cross-repository-contract-digest.json) 与[权威非生产联合验收](../artifacts/testing/non-production-joint-acceptance/non-production-joint-acceptance.json)绑定三仓最终 clean HEAD | 满足 |
| 13 | 正式规则沉淀 | 长期规则已进入总规则、项目规则或专题契约；项目滚动复盘仅保留经过和证据 | 满足 |
| 14 | 外部/生产边界真实记录 | 未执行 Windows 实机、现场、生产、LiveExternal、PR merge 或生产部署，未将其冒充为自动化完成 | 满足 |

当前有效结论以第 15 节“审计后重新闭合批次”为准。历史表中的“满足”不得用于覆盖第 15 节的 `OPEN`、`NOT-RUN` 或 `FAIL`。

## 14. 当前基线与总进度

### 14.1 2026-07-16 历史冻结基线

以下数字是重新打开前的冻结快照，用于回归比较，不是新一轮候选 HEAD 的最终证据：

- Edge：最终 clean HEAD `08eac58e3342571aef1759cd0188a79751c85d85`，tree `4aafe618a72a4d6161931e144a133664761fcac2`；PR #52 / run `29495673942` 全绿；32 runner、1280/1280、0 failed、0 skipped，coverage line `65.7581%`、branch `51.2107%`，两样本 p95 `21m23`。最终联合验收后已停止 build server 并清空可再生成目录。
- Cloud：最终 clean HEAD `ef7d950e83b7c119c173e4b800846bffb43f7e95`，tree `a8ed1fcae6c1be7f527e9de98343c72095d0a2d7`；PR #36 / run `29495656348` required job 全绿；inventory 18/725、required 16/661、Web 69、deployment 33，均 0 failed、0 skipped；coverage line `63.159887%`、branch `50.097882%`，当前样本 `12m19`。
- AICopilot：最终 clean HEAD `87b2336630125d6168b0b7efb5d4b4e8a97a2c60`，tree `4a8fa2ce99d7157791e02d197a04a0ca5cb56701`；Draft PR #60 / final run `29517763217` 全绿且 annotations 为 0；25 项目、1025 cases，required 17 项目/1011、manual 14；.NET 1011/1011、Vitest 165/165、Playwright 43/43、deployment 33/33，均 0 failed、0 skipped；coverage line `81.255899%`、branch `53.905398%`；mutation 58/58 killed，score `100%`；三样本 p95 `23m02`。
- 工作区：最终 contract `edge-pass-station-batch-v1` digest 为 `86cc7ca5399d6af3524ce34f2cace3ea926625b808b72f30f83b3118b8fa6d81`；跨仓 duplication 对 2252 个生产、51 个 support、431 个 test-case 源文件建立六组 report-only baseline；权威联合验收以 Release、`noBuild=false` 串行验证 Cloud provider 47/47、Edge consumer 87/87、Cloud-AI alignment 1/1，且 `productionOperations=false`、`deployChangedInvoked=false`。

### 14.2 当前正式进度

本计划采用 AND 门禁，不使用按文件数、代码量、运行时长、文档数、commit 数或已完成工作量折算的百分比。

2026-07-16 曾登记 **14/14、100%**；2026-07-17 客观审核得到 `PASS=7`、`FAIL=7`，随后行级复核又确认 AI RAG/MCP E2E 未被 xUnit 收集等真实缺口。因此当前唯一正式状态为 **`REOPENED / NOT COMPLETE`**，不再沿用 22%，也不公布新的中间百分比。

只有第 15 节全部 blocking 项绑定最终 clean HEAD、远端 required CI 和不可变证据后均为 `PASS`，才可重新登记 **14/14、100%**。任何 `FAIL` 或 `NOT-RUN` 都使总体保持未完成。

GitHub Organization/Enterprise、额外 reviewer、自定义授权链的调查或建设不计入完成度；它们已按纠偏要求从活动治理模型退役。Edge 宿主仓/插件仓拆分属于下一份独立计划，既不计本计划进度，也不阻塞本计划关单。

### 14.3 重新打开后的边界

本计划已重新打开，存在明确的下一执行波次。边界如下：

1. Edge PR #52 已合入 `main`；Edge 修复必须从最新 `origin/main` 创建新分支和新 Draft PR。Cloud PR #36、AICopilot PR #60 继续在各自权威 worktree/原分支上更新。
2. 用户已授权本批三仓代码、测试、规则、测试工作流、commit、push、Draft PR 和 GitHub CI/artifact 操作；在该范围内不得重复询权。
3. 授权不包含 PR merge、生产发布、上传 `stable`、`deploy/Deploy-Changed.ps1`、生产 CD/schema/data、服务器/Harbor/SSH、现场设备或 LiveExternal 操作。
4. Windows 实机、现场、真实 PLC/MES/Cloud/模型和生产验收仍是 `N/A`，不得冒充已执行，也不得阻塞本自动化批次。
5. Edge 宿主仓/插件仓拆分继续属于下一份独立计划，不进入本轮范围。

历史 HEAD、测试数量、coverage/mutation、CI 运行号、digest、duplication 和联合验收明细仅作为回归下界；新一轮终态必须重新生成并绑定新的候选 HEAD。

## 15. 2026-07-17 审计后重新闭合批次

### 15.1 唯一执行交接

本节是重新打开后的权威范围入口。精确 worktree、文件、负例、命令、并行波次、GitHub 权限和可复制开场指令见派生执行交接：

- [三项目测试架构治理审计后修复执行交接-20260717-0926.md](../artifacts/handoffs/2026-07-17/三项目测试架构治理审计后修复执行交接-20260717-0926.md)

该交接只展开本计划，不是第二份总计划。若交接与本文件的范围或完成定义冲突，以本文件为准；交接中的实时 HEAD、PR、run 和证据索引可在执行中更新。

### 15.2 Blocking 批次

| ID | 仓/层级 | 必须闭合的客观结果 | 初始状态 |
|---|---|---|---|
| `AI-RAG-E2E-EXEC-001` | AI | RAG 上传→索引→搜索→MCP 测试被 xUnit 实际发现和执行，并进入 inventory、迁移账本与 required CI | `OPEN` |
| `AI-COMPAT-SURFACE-RATCHET-001` | AI | encv1 所有公开 legacy surface 均有逐符号 caller ratchet；新增调用、漏登记 surface 和放宽阈值负例均失败 | `OPEN` |
| `CLOUD-ANALYZER-CONTRACT-BINDING-001` | Cloud | Analyzer 对真实 `Services.Contracts` metadata 成功绑定；缺失/改名时 fail-closed，正反 fixture 进入 required CI | `OPEN` |
| `CLOUD-COMPAT-SOURCE-BINDING-001` | Cloud/Edge | 外部 HEAD 作为证据身份记录，兼容性阻断以 clean consumer source-state digest、逐文件 SHA、pattern 和 call count 为语义判据 | `OPEN` |
| `EDGE-STARTUP-EXCEPTION-ALLOWLIST-001` | Edge | 启动适配器只降级显式可恢复异常；未知异常保持同一实例抛出；不伪造取消语义 | `OPEN` |
| `EDGE-DIECUT-ACTIVE-INPUT-ALLOWLIST-001` | Edge | 活动源码/配置/solution/打包输入模切零命中；41 条退役账本仅作为精确 allowlist 的迁移证据保留 | `OPEN` |
| `GOV-CLOSE-PREDICATE-CATALOG-001` | 工作区 | 14 项 AND 条件均有封闭输入集、allowlist/denylist、机器命令、阈值、HEAD/run/evidence 绑定和原因码 | `OPEN` |
| `GOV-CLOSE-EVIDENCE-ANCHOR-001` | 工作区/GitHub | 最终根证据、catalog、三仓 HEAD/run/artifact digest 被非生产 Git/GitHub 不可变对象锚定 | `OPEN` |
| `FINAL-ACCEPTANCE-REOPEN-001` | 三仓联合 | 三仓 clean candidate 的 required、coverage、mutation、duplication、compatibility、contract digest、非生产联合验收和严格 14 项复审全部 PASS | `NOT-RUN` |

### 15.3 关单公式

```text
重新关单 = 第 15.2 节所有 blocking 批次 PASS
           AND 三仓候选 worktree clean
           AND 远端 PR head OID = 本地候选 HEAD
           AND required discovered = executed = passed
           AND failed = skipped = 0
           AND 最终证据存在不可变 Git/GitHub 锚点
```

完成前禁止写“基本完成”“条件通过”“主体 100%”或折算百分比。完成后才更新第 13.1 节为新的最终矩阵，并保留 2026-07-16 历史快照供审计。
