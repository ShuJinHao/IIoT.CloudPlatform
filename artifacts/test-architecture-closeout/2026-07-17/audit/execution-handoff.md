# 三项目测试架构治理审计后修复执行交接

> 生成时间：2026-07-17 09:26:39 +0800  
> 状态：`READY FOR EXECUTION`  
> 性质：`docs/三项目测试架构治理总计划.md` 的派生执行交接，不是第二份总计划  
> 当前正式进度：`REOPENED / NOT COMPLETE`；不使用 22%、100% 或其它中间折算百分比  
> 执行目标：关闭 4 组真实代码/测试缺口、3 组治理证据缺口，重跑三仓及联合验收，并以不可变证据完成严格 14 项 AND 复审

## 1. 先读结论

三仓主体测试架构已经成立，不需要推倒重来。剩余代码改动属于定向修复，但最终闭合仍包含完整测试、GitHub CI、跨仓证据重绑和不可变锚定。

当前不能宣称总计划完成，原因不是“还有 78% 代码没写”，而是 AND 门禁中仍有 blocking 项未闭合。2026-07-16 的 14/14、100% 是历史登记快照；2026-07-17 后续审计已确认：

1. AI RAG/MCP E2E 方法缺少 `[Fact]`，从未被 xUnit 收集和执行。
2. AI encv1 compatibility 只对 `IsLegacyEncrypted` 建 caller ratchet，没有把 `ReEncryptLegacyCipher` 纳入同一公开迁移 surface。
3. Cloud Analyzer 的关键 metadata fixture 使用手写镜像接口，真实 `Services.Contracts` 漂移时存在 fail-open 风险。
4. Edge 若干同步启动 adapter 对未知异常降级过宽，缺少精确 recoverable allowlist。
5. Cloud compatibility 把通过条件绑成外部 Edge HEAD 精确相等，doc-only HEAD 漂移会误报；同时不能简单删除 HEAD gate而失去新增 consumer 检测。
6. Edge 的 41 条 `retired-diecut` 是正向退役证据，不是活动能力，但缺少正式、封闭、可执行的 allowlist 谓词。
7. 总计划第 1/5/8/9/10/13 项的输入全集、规则映射和证据索引不完全封闭；根证据位于非 Git 工作区，尚无不可变锚点。

整体完成公式只有一个：本交接第 13 节所有 blocking 条件全部 PASS，才重新登记 14/14、100%。

## 2. 本轮用户授权与不可扩展边界

### 2.1 已明确授权；不得重复询问

用户在当前会话明确授权本批：

- 同时修改 Edge、Cloud、AICopilot 三个项目中与本计划直接相关的代码、测试、baseline、规则、复盘和测试工作流。
- 修改工作区级 `docs/`、`scripts/testing/`、`artifacts/testing/` 和本交接所列治理证据文件。
- 创建分支和 worktree、普通 commit、普通 push、创建或更新 Draft PR。
- 修改和触发 GitHub **测试/治理 workflow**，读取 checks、annotations、logs、artifacts、digests 和 PR metadata。
- 在首次 CI 失败后诊断并修复；可以重新触发已修复的新 HEAD，但不得用 rerun 掩盖或删除首次失败证据。

新窗口不得再次询问“是否可以改三仓”“是否可以 commit/push”“是否可以改测试 workflow”“是否可以创建 Draft PR”。这些权限已经给出。

### 2.2 未授权；绝对禁止

- PR merge、转为 Ready、直接写入或推送 `main`。
- force push、删除用户分支、重写既有历史。
- `deploy/Deploy-Changed.ps1`、`deploy/Deploy.ps1`、`Invoke-WorkspaceDeploy.ps1` 或任何生产发布入口。
- 上传 `stable`、生产 CD、生产 schema/data、数据库迁移、服务器/Harbor/SSH、Windows 现场机、真实 PLC/MES/Cloud/模型、LiveExternal。
- Edge 宿主仓/插件仓拆分、GitHub Organization/Enterprise、额外 reviewer、CODEOWNERS 或自定义授权链。

测试 workflow 权限不能解释为生产 workflow 权限。若某个 push/tag 会触发生产 workflow，禁止该触发方式；本批不创建 tag。

### 2.3 只有这些是真正阻断

在上述授权范围内持续执行，不因普通分支、文件、测试或 GitHub workflow 权限停下。只有以下情况可报告阻断：

1. 现有 GitHub 凭据实际失效，且重用现有 `gh` 会话仍失败；若出现 device flow，必须立即把页面显示的准确 code 给用户，不能只给链接。
2. 发现用户并行未提交修改与本批同文件冲突，且无法通过保留双方内容安全合并。
3. 完成修复必须扩展到第 2.2 节禁止的生产/merge/拆仓范围。

不要预先启动 GitHub device flow；先执行 `gh auth status` 和只读查询。

## 3. 唯一规则入口与禁止读取项

启动时按顺序读取：

1. `/Users/shushu/Developer/产线系统架构升级/1/AGENTS.md`
2. `/Users/shushu/Developer/产线系统架构升级/1/docs/总规则.md`
3. `/Users/shushu/Developer/产线系统架构升级/1/docs/三项目测试架构治理总计划.md`，重点第 12、13、15 节
4. 本交接
5. `/Users/shushu/Developer/产线系统架构升级/1/artifacts/audits/2026-07-17/三项目测试架构治理-客观终审核查清单-20260717.md`，只作为重新打开的审计输入，不把旧 HEAD 当新终态
6. Edge：`docs/客户端规则.md`、`docs/Edge架构边界契约.md`
7. Cloud：工作树内 `AGENTS.md`、`docs/云端规则.md`、`docs/contracts/cloud-architecture.md`
8. AI：工作树内 `AGENTS.md`、`资料/AICopilot业务规则.md`
9. 每仓本批源码、测试、workflow 和近期 Git/GitHub 历史

禁止读取或使用：

- `/Users/shushu/Developer/产线系统架构升级/1/docs/三项目源码审计与架构治理执行计划.md`

该文件是归档计划，不是当前范围或进度来源。历史复盘只按本批类型、Rule ID 或失败症状定向检索；不能用历史材料替代现行规则。

2026-07-16 的旧续跑交接已由本文取代，只能用于追溯旧 run/证据，不再执行其中的“下一命令”或进度结论。

## 4. 当前冻结锚点

以下是开始执行前的只读锚点。任何新提交都会使旧 run/artifact 失去最终证据资格。

| 仓 | 权威工作树 | 分支 | HEAD | tree | 远端状态 |
|---|---|---|---|---|---|
| Edge | `/Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient` | `main` | `e980d8cb3092582dfe08f96b19abde34b6804cef` | `4aafe618a72a4d6161931e144a133664761fcac2` | clean；PR #52 已 MERGED |
| Cloud | `/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001` | `agent/cloud-test-architecture-20260714` | `ef7d950e83b7c119c173e4b800846bffb43f7e95` | `a8ed1fcae6c1be7f527e9de98343c72095d0a2d7` | clean；Draft PR #36 OPEN |
| AI | `/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/ai-phase0-closeout` | `agent/ai-test-architecture-20260714` | `87b2336630125d6168b0b7efb5d4b4e8a97a2c60` | `4a8fa2ce99d7157791e02d197a04a0ca5cb56701` | clean；stacked Draft PR #60 OPEN |

Cloud PR #36 当前 base 为 `main@88c41109fbcf0b87b18939a139e0bff751e03d07`。AI PR #60 当前 base 为 `agent/ai-workflow-branch-failures@198cc59318f4a1748c719b9b8ecff1d969952ce8`，不得擅自 retarget 到 `main`。

禁止在以下默认目录执行本批写入，它们是干净但非候选分支：

- `/Users/shushu/Developer/产线系统架构升级/1/IIoT.CloudPlatform`
- `/Users/shushu/Developer/产线系统架构升级/1/AICopilot`

旧最终 run `29495656348`、`29517763217` 和旧三仓联合证据只能作历史比较；新 HEAD 必须有新 CI 和新证据。

## 5. 并行拓扑与合流门槛

同一仓同一时刻只允许一个 writer。建议主窗口作为协调者，三个子代理各写一个仓；共享根目录只由主窗口写。

```text
Wave 0  主协调者：冻结状态、创建 Edge worktree、冻结 predicate catalog schema
            │
            ├── AI writer：RAG E2E + encv1 caller ratchet
Wave 1      ├── Cloud writer：真实 Contracts fixture + compatibility 通用实现
并行        └── Edge writer：异常 allowlist + retired-diecut 机器谓词
            │
            └── 依赖：Edge 形成 clean candidate commit 后，Cloud 才填写最终 Edge SHA/src-state digest
            │
Wave 2  各仓 targeted → full required → quality no-regression → 复盘/规则
            │
Wave 3  commit/push；Edge 新 Draft PR，Cloud #36、AI #60 更新；等待新 HEAD 首次 CI
            │
Wave 4  三仓 clean candidate → contract digest → duplication → 非生产联合验收
            │
Wave 5  14 项机器 catalog → evidence-only Git 锚点 → 独立严格复审 → 14/14、100%
```

禁止三个 writer 同时修改根总计划、根 catalog 或根证据。Cloud writer在 Edge 最终 SHA 未冻结前只能完成通用代码和 fixture，不得把当前旧 SHA 当最终值提交。

## 6. Wave 0：启动与 Edge 新分支

### 6.1 状态检查

```bash
git -C /Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient status --short --branch
git -C /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001 status --short --branch
git -C /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/ai-phase0-closeout status --short --branch
gh auth status
```

三仓必须 clean。若 GitHub PR base/head 已移动，以 GitHub 当前值更新本交接的运行记录，不修改历史锚点。

### 6.2 创建 Edge 权威修复 worktree

```bash
git -C /Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient fetch origin main
git -C /Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient worktree add \
  -b agent/edge-startup-exception-retirement-closure-20260717 \
  /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/edge-startup-exception-retirement-closure \
  origin/main
```

后续 Edge writer 只写这个新 worktree。若分支名已存在，先确认它是否正是本批且 clean；不得删除或覆盖未知分支。

## 7. AI writer：两组真实修复

工作树：`/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/ai-phase0-closeout`

### 7.1 `AI-RAG-E2E-EXEC-001`

必须修改：

- `src/tests/AICopilot.EndToEndTests/RagMcpEndToEndTests.cs`
- `scripts/tests/baselines/aicopilot-test-cases.json`，只能由真实 discovery 更新，禁止手写数字
- `scripts/tests/baselines/aicopilot-test-declaration-transition.json`
- `scripts/tests/Test-AICopilotTestDeclarationTransition.ps1`

实现口径：

1. 在 `RagAndMcpSmoke_ShouldIndexDocument_SearchContent_AndLoadOnlyEnabledMcp` 前恢复 `[Fact]`，不削弱测试体和断言。
2. `--list-tests` 对完整 FullyQualifiedName 必须恰好命中 1；先证明非 0-hit，再执行。
3. 迁移账本把旧 Phase25 RAG/MCP 条目从 `replaced` 改为 `retained-self`，唯一 replacement 为恢复执行的 E2E identity。
4. 冻结 source 仍为 commit `198cc59318f4a1748c719b9b8ecff1d969952ce8`、tree `88aee67db521a1a33ff6de524c0163d513396123`、声明 785；不得改写历史 source 锚点。
5. 预期 current declaration `768→769`、retained `632→633`、replaced `151→150`、transition 仍 785。transition hash 必须复用现有 NUL/LF 规范重新计算并同时写入 ledger 和 checker。
6. 真实 inventory 预期全部 1026、required 1012、E2E 25、required runner 17、manual 14；最终以 discovery 输出为准，不为守数字删/Skip 测试。

### 7.2 `AI-COMPAT-SURFACE-RATCHET-001`

必须修改：

- `scripts/tests/aicopilot-compatibility-inventory.json`
- `scripts/tests/tools/AICopilot.CompatibilitySymbolProbe/Program.cs`
- `scripts/tests/Test-AICopilotTestInfrastructureBehavior.ps1`
- `AGENTS.md`
- `docs/AI架构治理清单.md`
- `docs/改动复盘与规则沉淀.md`

实现口径：

1. `AI-COMPAT-SECRET-ENCV1/primary` 的 symbol union 同时覆盖：
   - `SecretStringEncryptor.IsLegacyEncrypted(System.String)`
   - `SecretStringEncryptor.ReEncryptLegacyCipher(System.String)`
2. 排除 producer 文件 `src/infrastructure/AICopilot.EntityFrameworkCore/Security/SecretStringEncryptor.cs`。
3. 新增可选 `distinct-caller-member` 计数：对 union 命中的外部引用按非空 `EnclosingMemberSymbolId` 去重；解析不到 enclosing member 必须 fail-closed。
4. 当前两个 migration worker 各自使用两个 surface，结果仍为 2。不能把 baseline 上限从 2 放宽到 4。
5. 四个行为 fixture：同 caller 用两个 surface 计 1；第三 caller 只调 `IsLegacyEncrypted` 失败；第三 caller 只调 `ReEncryptLegacyCipher` 失败；未知 count mode 失败。基础设施行为用例预期 `98→102`。

明确禁止修改：

- `scripts/tests/baselines/aicopilot-compatibility.json`，`maximumCallSites=2` 和当前 baseline SHA 必须不变；禁止 compatibility `-UpdateBaseline`。
- `SecretStringEncryptor.cs` 和两个 migration worker 的生产行为。
- `.github/workflows/aicopilot-ci.yml`、solution/csproj、coverage/duplication/mutation baseline，除非真实失败证明现有动态发现有缺陷；不能为守绿放宽。

### 7.3 AI 定向验证

```powershell
$BaseSha = '198cc59318f4a1748c719b9b8ecff1d969952ce8'
$Filter = 'FullyQualifiedName=AICopilot.EndToEndTests.RagMcpEndToEndTests.RagAndMcpSmoke_ShouldIndexDocument_SearchContent_AndLoadOnlyEnabledMcp'

dotnet restore AICopilot.slnx
dotnet build AICopilot.slnx -c Release --no-restore

pwsh -NoProfile -File scripts/tests/Get-AICopilotTestInventory.ps1 `
  -Configuration Release -BaseRef $BaseSha -UpdateBaseline `
  -OutputPath artifacts/test-inventory.json
pwsh -NoProfile -File scripts/tests/Get-AICopilotTestInventory.ps1 `
  -Configuration Release -BaseRef $BaseSha `
  -OutputPath artifacts/test-inventory.json

dotnet test src/tests/AICopilot.EndToEndTests/AICopilot.EndToEndTests.csproj `
  -c Release --no-build --no-restore --list-tests --filter $Filter
dotnet test src/tests/AICopilot.EndToEndTests/AICopilot.EndToEndTests.csproj `
  -c Release --no-build --no-restore --filter $Filter
dotnet test src/tests/AICopilot.EndToEndTests/AICopilot.EndToEndTests.csproj `
  -c Release --no-build --no-restore

pwsh -NoProfile -File scripts/tests/Test-AICopilotTestInfrastructureBehavior.ps1
pwsh -NoProfile -File scripts/tests/Test-AICopilotTestDeclarationTransition.ps1
pwsh -NoProfile -File scripts/tests/Test-AICopilotCompatibilityInventory.ps1 `
  -BaseRef $BaseSha -OutputPath artifacts/quality/aicopilot-compatibility.json
pwsh -NoProfile -File scripts/tests/Measure-AICopilotDuplication.ps1 `
  -BaseRef $BaseSha -OutputPath artifacts/quality/aicopilot-duplication.json
```

必须额外证明 compatibility baseline 没变：

```bash
git diff --exit-code 87b2336630125d6168b0b7efb5d4b4e8a97a2c60 -- \
  scripts/tests/baselines/aicopilot-compatibility.json
git diff --check
```

AI 完成谓词：精确 E2E filter 命中并通过；E2E 25/25；required 1012/1012、0 failed、0 skipped；transition 输出 `785/769/633/150/0/2/0`；compatibility 仍为 2 且任一 surface 的第三 caller 失败；五个新 PR job 绑定最终 HEAD 全部 SUCCESS。

## 8. Cloud writer：真实契约 fixture 与稳定 consumer 绑定

工作树：`/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001`

### 8.1 `CLOUD-ANALYZER-CONTRACT-BINDING-001`

客观问题是 fixture 手写镜像真实契约，而不是 CLOUDARCH008/009 名称启发式。不要修改 CLOUDARCH008/009，也不要给所有不引用 Contracts 的项目增加全局“缺符号”诊断。

本批的封闭 identity 集合是 Analyzer 当前引用的全部 9 个 `IIoT.Services.Contracts` metadata name：``IHumanRequest`1``、``IDeviceRequest`1``、``IAnonymousBootstrapRequest`1``、``IPublicRequest`1``、``IAiReadRequest`1``、`ICacheService`、`Authorization.IPermissionProvider`、`Authorization.IDevicePermissionService`、`RecordQueries.IDeviceIdentityQueryService`。不能只证明其中两个。

最小修改：

- `scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh`
- `src/tests/IIoT.CloudPlatform.DeploymentTests/DeploymentSourceGuardTests.cs`

要求：

1. `Invalid009` 和 `ValidDelegateContext` 的 generated csproj `ProjectReference` 真实 `src/services/IIoT.Services.Contracts/IIoT.Services.Contracts.csproj`。
2. 删除两 fixture 中手写的 `ICacheService`、`IPermissionProvider` 镜像，按真实方法签名实现。
3. `Invalid009` 仍只因真实 `ICacheService.GetOrSetAsync<T>` 安全缓存路径产生 `CLOUDARCH007`。
4. `ValidDelegateContext` 使用真实契约且零诊断。
5. 原位替换 fixture，保持 `valid=8 invalid=15` 和 Cloud required 661，不新增 `[Fact]`。
6. fixture gate 对真实 Contracts build output运行 metadata binding probe，9 个 identity 必须各解析为恰好一个类型；任一缺失/改名必须非零。
7. 既有 `DeploymentSourceGuardTests` 同一测试内提取 Analyzer 的 `IIoT.Services.Contracts` metadata literals，断言集合恰好等于上述 9 项，并锁定真实 Contracts ProjectReference、binding probe 和原 fixture 汇总。这样新增第 10 个 identity也不能在未纳入 probe 时静默出现。

真实契约 namespace、接口名或签名漂移而 Analyzer/fixture 未同步时，必须因编译失败或预期诊断缺失而非零退出。

### 8.2 `CLOUD-COMPAT-SOURCE-BINDING-001`

修改：

- `scripts/tests/Test-CloudCompatibility.ps1`
- `scripts/tests/baselines/cloud-compatibility.json`
- `.github/workflows/cloud-ci.yml`
- `src/tests/IIoT.CloudPlatform.DeploymentTests/DeploymentSourceGuardTests.cs`
- `docs/云端规则.md`
- `docs/contracts/cloud-architecture.md`
- `docs/云端架构治理清单.md`
- `docs/改动复盘与规则沉淀.md`

安全语义：

1. 实测 Edge HEAD 和 clean 状态必须记录；dirty 仍立即失败。
2. HEAD 不再是单独的 PASS/FAIL 条件，但 workflow 必须 checkout 一个不可变 full SHA。
3. baseline 升 schema 4，记录：binding、reference HEAD、整个 tracked `src/` source-state SHA-256、两份已声明 consumer 的聚合 digest、逐文件 SHA、pattern count、`RequestId` absence。
4. 整个 `src/` universe 变化必须失败，防止在第三个文件新增 consumer 后绕过现有两文件清单。
5. reference HEAD 不同但整个 `src/` 和 consumer state 完全相同的 doc-only 变化必须通过，并报告 `headMatchesReference=false`。
6. consumer path/SHA/pattern/must-not-contain/aggregate digest、仓 clean 状态、`src/` state 任一漂移均失败。
7. 正反行为 fixture 必须覆盖上述条件，不能只是删掉旧 HEAD 检查。

依赖门槛：Cloud 可先完成 schema、脚本和 fixture，但 `externalEvidenceReferenceHead`、workflow `ref` 和 repository source-state 必须等 Edge writer 形成最终 clean candidate commit 后再填写。不得硬编码当前 `08eac58...` 或 `e980d8...` 为本轮最终值，因为 Edge 本批会修改 `src/`。

### 8.3 Cloud 定向与全量验证

```bash
bash scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh
dotnet restore src/tests/IIoT.CloudPlatform.DeploymentTests/IIoT.CloudPlatform.DeploymentTests.csproj \
  --disable-build-servers --nologo -noAutoResponse
dotnet test src/tests/IIoT.CloudPlatform.DeploymentTests/IIoT.CloudPlatform.DeploymentTests.csproj \
  -c Release --no-restore --disable-build-servers --nologo -noAutoResponse
pwsh -NoProfile -File scripts/tests/Test-CloudCompatibility.ps1 \
  -EdgeRepositoryRoot /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/edge-startup-exception-retirement-closure \
  -BaseRef 88c41109fbcf0b87b18939a139e0bff751e03d07 \
  -ReportDirectory /private/tmp/cloud-compatibility-final
```

完整候选验证：

```bash
dotnet restore IIoT.CloudPlatform.slnx --disable-build-servers --nologo -noAutoResponse
dotnet build IIoT.CloudPlatform.slnx -c Release --no-restore \
  --disable-build-servers --nologo -noAutoResponse \
  -property:RunAnalyzers=true -property:RunAnalyzersDuringBuild=true
bash scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh
pwsh scripts/tests/Test-CloudTestMigration.ps1
pwsh scripts/tests/Test-CloudQualityBaselineProtectionBehavior.ps1
pwsh scripts/tests/Test-CloudCompatibility.ps1 \
  -EdgeRepositoryRoot /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/edge-startup-exception-retirement-closure \
  -BaseRef 88c41109fbcf0b87b18939a139e0bff751e03d07
pwsh scripts/tests/Test-CloudDuplication.ps1 -BaseRef 88c41109fbcf0b87b18939a139e0bff751e03d07
pwsh scripts/tests/Invoke-CloudTestInventory.ps1 -Mode Required -Configuration Release -NoBuild -CollectCoverage
pwsh scripts/tests/Test-CloudCoverage.ps1 -BaseRef 88c41109fbcf0b87b18939a139e0bff751e03d07
git diff --check
```

Cloud 完成谓词：真实 Contracts 正反 fixture闭合且汇总不变；doc-only HEAD 漂移 PASS；任意 Edge `src/`、consumer 语义或 clean 状态漂移 FAIL；workflow pin 等于本轮最终 Edge full SHA；Cloud 661/661、0 failed、0 skipped；PR #36 required job 绑定新 Cloud HEAD 成功。

## 9. Edge writer：同步启动异常与模切证据

工作树：`/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/edge-startup-exception-retirement-closure`

### 9.1 `EDGE-STARTUP-EXCEPTION-ALLOWLIST-001`

准确口径：这些是同步 filesystem/plugin adapter，没有 CancellationToken；不得把问题描述成“吞取消”。真实目标是已批准输入/IO异常继续非阻断，未知异常和直接 OCE 原实例传播。

生产范围：

- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/ShellConfigurationLoader.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/ShellRuntimePathResolver.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/DirectoryModuleCatalog.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/ModulePluginLoader.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/ModulePluginAssemblyResolver.cs`
- `src/Edge/IIoT.Edge.Shell/Modules/ShellModuleCatalog.cs`
- `src/Edge/IIoT.Edge.Host.Bootstrap/Core/EdgeRuntimePathPreflight.cs`

实现规则：

1. filesystem/path 边界只翻译 `ArgumentException`、`NotSupportedException`、`IOException`、`UnauthorizedAccessException`、`SecurityException`。
2. `ShellConfigurationLoader` 对 `_moduleCatalog.DiscoverModules` 的外层 broad catch 删除；catalog 只返回批准的 plugin input issue，未知实现异常直接传播。
3. 新增明确的 `ModulePluginManifestException`、`ModulePluginLoadException` 边界；批准的 manifest/reflection/assembly/filesystem 契约失败可包装并保留 inner exception。
4. Catalog 激活只捕获 typed plugin failure；未知异常和直接 OCE 不捕获。
5. resolver/preflight 使用 internal delegate seam 做确定性 fault injection，默认构造和生产路径不变；每个 probe/loader 最多执行一次。
6. 不顺手批量修改 `AppLifecycleManager`、`StartupDiagnosticsReportBuilder`、`CrashLogWriter` 等语义不同的聚合/最终日志边界。

测试：

- `src/Tests/IIoT.Edge.Shell.FilesystemTests/ShellConfigurationLoaderBehaviorTests.cs`
- `src/Tests/IIoT.Edge.Shell.FilesystemTests/ShellRuntimePathResolverBehaviorTests.cs`
- `src/Tests/IIoT.Edge.Shell.FilesystemTests/CoreStabilityBehaviorTests.cs`
- `src/Tests/IIoT.Edge.Module.ConformanceTests/PluginCatalogLifecycleContractTests.cs`
- `src/Tests/IIoT.Edge.Module.ConformanceTests/ShellModuleCatalogExternalPluginBehaviorTests.cs`
- `src/Tests/IIoT.Edge.Startup.IntegrationTests/ModuleRuntimeRegistrationTests.cs`

每个边界都要有：批准异常→稳定诊断/fallback/等价启动；自定义未知异常→`Assert.Same`；直接 OCE→`Assert.Same`；独立有效插件不被坏插件阻断；无重复副作用。

### 9.2 `EDGE-DIECUT-ACTIVE-INPUT-ALLOWLIST-001`

必须修改/新增：

- `scripts/tests/baselines/edge-regression-ledger.json`
- `scripts/tests/Test-EdgeRegressionLedger.ps1`
- 新增 `scripts/tests/Test-EdgeRetiredFeatureEvidence.ps1`
- 新增 `scripts/tests/Test-EdgeRetiredFeatureEvidenceFixtures.ps1`
- `.github/workflows/edge-smoke-build.yml`
- `.github/workflows/edge-pack-modules.yml`，只接入 preflight；本批不触发 pack/release/deploy
- `docs/客户端规则.md`
- `docs/Edge架构边界契约.md`
- `docs/改动复盘与规则沉淀.md`

机器谓词：

1. ledger 升 schema，新增 `EDGE-DIECUT-RETIRE-001` retirement evidence：disposition=`retired-diecut`、expectedDeclarationCount=41、固定 4 个历史 oldSourcePath、受控 token/path pattern。
2. 非文档治理 allowlist 精确只有：
   - `scripts/tests/Test-EdgeRegressionLedger.ps1`
   - `scripts/tests/baselines/edge-regression-ledger.json`
3. `src`、测试、solution/project、配置、UI、`.github`、打包/发布输入和其它脚本任何第三处模切 token/path 命中都失败。
4. 41 条全部绑定既有冻结 source commit/tree，oldKey 唯一、replacement 精确、reason 非空，不得回流 current discovered declarations。
5. 负例至少包括：活动源码回流、第三治理文件、40/42 条、重复 oldKey、错误 disposition/decision、old declaration 回流；均必须非零退出。

### 9.3 Edge 定向验证

```bash
dotnet restore IIoT.EdgeClient.slnx --disable-build-servers --nologo -noAutoResponse

dotnet build src/Tests/IIoT.Edge.Shell.FilesystemTests/IIoT.Edge.Shell.FilesystemTests.csproj \
  -c Release --no-restore -t:Rebuild --disable-build-servers --nologo -noAutoResponse
dotnet test src/Tests/IIoT.Edge.Shell.FilesystemTests/IIoT.Edge.Shell.FilesystemTests.csproj \
  -c Release --no-build --disable-build-servers --nologo

dotnet build src/Tests/IIoT.Edge.Module.ConformanceTests/IIoT.Edge.Module.ConformanceTests.csproj \
  -c Release --no-restore -t:Rebuild --disable-build-servers --nologo -noAutoResponse
dotnet test src/Tests/IIoT.Edge.Module.ConformanceTests/IIoT.Edge.Module.ConformanceTests.csproj \
  -c Release --no-build --disable-build-servers --nologo

dotnet build src/Tests/IIoT.Edge.Startup.IntegrationTests/IIoT.Edge.Startup.IntegrationTests.csproj \
  -c Release --no-restore -t:Rebuild --disable-build-servers --nologo -noAutoResponse
dotnet test src/Tests/IIoT.Edge.Startup.IntegrationTests/IIoT.Edge.Startup.IntegrationTests.csproj \
  -c Release --no-build --disable-build-servers --nologo

pwsh -NoProfile -File scripts/tests/Test-EdgeRetiredFeatureEvidenceFixtures.ps1 -RepositoryRoot .
pwsh -NoProfile -File scripts/tests/Test-EdgeRetiredFeatureEvidence.ps1 -RepositoryRoot .
pwsh -NoProfile -File scripts/tests/Test-EdgeRegressionLedger.ps1 -RepositoryRoot .
git diff --check
```

随后完整复现 `.github/workflows/edge-smoke-build.yml` 的 inventory、project graph、compatibility/fixtures、duplication、source quality、Analyzer fixture、Release solution build、完整 required+coverage、结果确认和 coverage ratchet。新增测试后不得继续硬报旧 1280；以真实 discovered/executed 对账为准。

Edge 完成谓词：本批 adapter 无 bare/unfiltered broad catch；批准异常非阻断，未知/OCE 同实例传播；模切活动输入零命中；非文档治理命中集合精确等于两文件；retired-diecut 精确 41 且无回流；全部负例非零；新 Draft PR 的 smoke-build 绑定新 HEAD 成功；证据保存后受审清理可再生成目录回到 0。

## 10. Wave 2：各仓全量、质量门禁和复盘

每个 writer 必须完成：

1. targeted red/green 证据，保留首次真实失败。
2. 当前仓完整 Release build 和 required inventory；`discovered=executed=passed`、failed=0、skipped=0。
3. Analyzer/static/project graph 正反 fixture。
4. coverage、mutation、duplication、compatibility 全部使用 no-update/不放宽模式；只有真实新增测试 inventory 可按项目既有机制更新。
5. `git diff --check`、secret/Skip/Ignore/放宽断言扫描、完整 diff 审核。
6. 更新对应滚动复盘，最新记录置顶：范围、原因、影响、验证命令、结果、长期规则结论。
7. 形成的长期规则同步到本交接指定正式规则位置；历史复盘不能成为唯一规则来源。

项目复盘位置：

- Edge：`docs/改动复盘与规则沉淀.md`
- Cloud：`docs/改动复盘与规则沉淀.md`
- AI：`docs/改动复盘与规则沉淀.md`

## 11. Wave 3：commit、push、Draft PR 与新 CI

### 11.1 Edge

建议三个普通 commit：

1. `fix(edge): narrow synchronous startup exception translation`
2. `test(edge): formalize retired diecut evidence predicate`
3. `docs(edge): record audit closure rules`

普通 push 后创建基于 `main` 的新 Draft PR。不得重开或复用已合并的 #52，不 stacked，不 merge。

### 11.2 Cloud

在原分支追加范围明确的 commit并普通 push，更新 Draft PR #36。不得新开平行 Cloud PR。最终 commit前必须使用 Edge 新 candidate full SHA 和新 `src/` source-state，不能沿用旧值。

### 11.3 AI

建议：

```bash
git add <本批精确文件>
git commit -m "test(ai): restore RAG E2E and close legacy caller ratchet"
git push origin agent/ai-test-architecture-20260714
```

更新原 stacked Draft PR #60；禁止 force push、禁止 retarget。

### 11.4 远端完成标准

- PR `headRefOid` 必须等于本地最终 HEAD。
- 新 HEAD 首次完整 required run 成功；旧 run/artifact 不能充当最终证据。
- Edge 新 PR smoke-build 成功；mutation-report如实记录。
- Cloud #36 required `cloud-ci / build-test` 成功，661/661、0 failed、0 skipped。
- AI #60 的 `governance-gates`、`dotnet-tests`、`web-deployment-tests`、`mutation-gate`、`build-test` 全部 SUCCESS；required .NET 1012/1012。
- Playwright/browser 产品断言必须由干净 GitHub runner 真实执行；不得把安装成功、list-tests 或旧 artifact 写成执行成功。
- 所有 annotations、artifact digest、run event/attempt/headSha 存档。

若新 CI 首次失败，保存 run ID、job、annotation、原始日志和 artifact，再修复后产生新 commit/new run；不得只 rerun 同一失败 SHA 来覆盖首次失败。

## 12. Wave 4/5：治理闭合与不可变证据

### 12.1 `GOV-CLOSE-PREDICATE-CATALOG-001`

主协调者在根目录新增：

- `scripts/testing/baselines/three-repository-closure-predicates.json`
- `scripts/testing/Test-ThreeRepositoryClosurePredicates.ps1`
- `scripts/testing/Test-ThreeRepositoryClosurePredicatesBehavior.ps1`
- `docs/testing/README.md` 的唯一入口索引
- 生成 `artifacts/testing/closure/three-repository-closure-result.json`

catalog 必须恰好覆盖总计划 14 项，至少包含：

- `id`、`blocking`、`description`
- 封闭 `inputUniverse`，不能只有“全部/零/真实/稳定”等自然语言
- 精确 allowlist/denylist、glob/root、排除理由
- 可执行 command、预期退出码/阈值、实际结果
- Edge/Cloud/AI head/tree/clean、PR/run/job/artifact binding
- evidence path、SHA-256、schema version、reason code、status
- rule→正式规则位置→自动门禁映射

行为 fixture 必须让以下情况非零：缺任一 1..14、重复 ID、空 input universe、未解释 allowlist、证据文件/hash 不匹配、candidate head mismatch、blocking=`NOT-RUN/FAIL`、规则只存在于复盘、使用未封闭全称词。

第 1/5/8/9/10/13 项至少分别封闭：

1. 错误治理资产 denylist 与允许保留位置。
2. 旧桶/filter/helper denylist 与重复 helper 定义。
3. 高风险 identity→test→runner→result→HEAD 映射。
4. TestKind/Runtime/Capability/ProjectReference 允许矩阵和违规 fixture。
5. duplication/coverage/mutation/compatibility baseline、阈值、最终 HEAD/run。
6. 所有新增长期规则的 formal location 和 gate。

### 12.2 三仓 clean candidate 联合证据

只有三仓代码、规则和复盘全部 commit，worktree clean，且远端 required CI 绑定相同 HEAD 后才运行：

```powershell
$Edge = '/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/edge-startup-exception-retirement-closure'
$Cloud = '/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001'
$Ai = '/Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/ai-phase0-closeout'

pwsh -NoProfile -File scripts/testing/Test-CrossRepositoryContractDigest.ps1 `
  -EdgeRepositoryRoot $Edge -CloudRepositoryRoot $Cloud `
  -OutputPath artifacts/testing/cross-repository-contract-digest.json

pwsh -NoProfile -File scripts/testing/Report-CrossRepositoryDuplication.ps1 `
  -EdgeRepositoryRoot $Edge -CloudRepositoryRoot $Cloud -AiRepositoryRoot $Ai `
  -ReportDirectory artifacts/testing/cross-repository-duplication

pwsh -NoProfile -File scripts/testing/Invoke-ThreeRepositoryNonProductionAcceptance.ps1 `
  -EdgeRepositoryRoot $Edge -CloudRepositoryRoot $Cloud -AiRepositoryRoot $Ai `
  -Configuration Release `
  -ResultsDirectory artifacts/testing/non-production-joint-acceptance
```

联合验收不得使用 `-NoBuild`，最终 JSON 必须为 Release、`noBuild=false`、`productionOperations=false`、`deployChangedInvoked=false`，所有 stage failed=0、skipped=0。

### 12.3 `GOV-CLOSE-EVIDENCE-ANCHOR-001`

工作区根不是 Git 仓，不能只用本地 SHA 宣称不可变。采用以下唯一方案，不创建 tag：

1. Cloud candidate #36 完全冻结后，单独创建 evidence worktree，不能在 #36 工作树切分支：

   ```bash
   git -C /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001 fetch origin main
   git -C /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001 worktree add \
     -b evidence/test-architecture-closeout-20260717 \
     /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-test-architecture-evidence-20260717 \
     origin/main
   ```

2. evidence 分支只包含 `artifacts/test-architecture-closeout/2026-07-17/` 下的证据副本和 manifest，不修改 Cloud 候选 PR #36。
3. 先生成 evidence commit A：冻结 closure catalog/result、三仓 HEAD/tree/PR/run/check/artifact digest、contract digest、duplication、联合验收 JSON、关键原始报告、重新打开状态的总计划快照和全部 SHA-256 manifest。
4. 普通 push commit A，并创建明确标注 `evidence-only / do not merge / no production` 的 Draft PR，使提交保持远端可达。三仓 candidate HEAD 不包含 evidence commit，避免自引用。
5. 以 commit A 做严格 14 项复审；只有全部 blocking PASS 后，才把根总计划更新为新的 14/14、100%，并写入 commit A 的 full SHA/PR 作为关单依据。
6. 再生成 evidence commit B：加入已关单总计划快照、最终严格审核报告、commit A 元数据及新 manifest。普通 push后，commit B 的 full SHA/tree和 manifest SHA是最终不可变锚点；根总计划不需要再写入 commit B，避免无限自引用。
7. 推送前审计 `.github/workflows/**` 的 branch/PR trigger，确认不会调用任何生产 deploy；若存在生产触发，不能用该分支，改用另一现有仓的等价 evidence-only Draft PR。禁止 tag。
8. 最终记录 evidence Draft PR URL、commit A/B、tree、manifest SHA 和审核者复算结果。

禁止把 evidence-only PR merge 到 main；它的目的只是提供远端 Git object 与可复核索引。

### 12.4 最终严格复审

基于新候选和 evidence anchor 重生成客观终审核查清单。状态只能是 `PASS/FAIL/NOT-RUN/N/A`；禁止加权分和“基本完成”。审核者必须主动尝试推翻：

- RAG E2E 是否真的被发现和执行，而非仅有 `[Fact]` 文本。
- encv1 任一公开 surface 是否可新增第三 caller 绕过。
- Cloud 真实 Contracts 漂移是否会使 fixture fail-closed。
- Edge 未知异常是否仍被翻译；模切是否在 allowlist 外回流。
- Cloud compatibility 是否既允许 doc-only SHA 漂移，又阻断任意 tracked `src/` 变化。
- 14 项 catalog 是否封闭且全部绑定最终 HEAD/run/evidence。
- 根证据是否可由远端 Git commit和 SHA manifest独立复算。

## 13. 最终完成清单

以下每一项都是 blocking：

- [ ] AI RAG/MCP E2E 精确 discovery=1，E2E 25/25，required 1012/1012，0 failed、0 skipped。
- [ ] AI transition 785/769/633/150/0/2/0，历史 source 锚点不变。
- [ ] AI encv1 两公开 surface 同一 ratchet，现值/上限仍为 2，四个行为负例通过。
- [ ] Cloud 两个真实 Contracts fixture正反闭合，valid=8/invalid=15，required 661/661。
- [ ] Cloud compatibility 绑定本轮最终 Edge immutable SHA；doc-only变化 PASS，任意 tracked `src`/consumer/dirty变化 FAIL。
- [ ] Edge批准异常非阻断，未知/OCE同实例传播，真实启动等价集成测试通过。
- [ ] Edge活动模切输入零命中；治理 allowlist 精确两文件；retired-diecut精确41且无回流。
- [ ] Edge/Cloud/AI 全部 candidate clean，PR head OID=本地 HEAD，新 required CI SUCCESS。
- [ ] 三仓 coverage/mutation/duplication/compatibility 无放宽、无回退。
- [ ] 三仓复盘与正式规则同步，根 predicate catalog 14/14 schema/behavior gate通过。
- [ ] contract digest、跨仓 duplication、Release 非生产联合验收绑定同一组三仓 clean HEAD。
- [ ] evidence-only commit/树/PR/manifest提供远端不可变锚点。
- [ ] 独立严格 14 项复审全部 blocking=`PASS`，无 `FAIL/NOT-RUN`。
- [ ] 未 merge、未部署、未调用生产 CD/Schema/Data/服务器/现场操作。
- [ ] Edge 最终证据保存后停止 build server，并按项目受审方式把可再生成目录恢复为 0。

只有全部勾选后，才把总计划当前状态改回“14/14、100%、已关单”。

## 14. 新窗口可直接复制的开场指令

```text
继续执行三项目测试架构治理的审计后修复闭环。

唯一权威总计划：
/Users/shushu/Developer/产线系统架构升级/1/docs/三项目测试架构治理总计划.md

精确执行交接：
/Users/shushu/Developer/产线系统架构升级/1/artifacts/handoffs/2026-07-17/三项目测试架构治理审计后修复执行交接-20260717-0926.md

先完整读取 AGENTS.md、总规则、总计划第 12/13/15 节、本交接和三仓当前规则；禁止读取归档的 docs/三项目源码审计与架构治理执行计划.md。

这是持续 goal 执行，不要停在解释或计划复述。用户已在原会话明确授权：三仓相关代码/测试/规则/复盘修改，工作区级治理文件，Git commit/push，创建或更新 Draft PR，GitHub 测试/治理 workflow、checks、logs、artifacts。上述范围不得再次询权。

权限明确不含：merge、force push、生产部署、Deploy-Changed、stable、生产 CD/schema/data、服务器/Harbor/SSH、现场设备、真实 PLC/MES/Cloud/模型、LiveExternal、拆仓。

使用四路并行：主协调者独占根 docs/scripts/artifacts；AI writer 独占 .codex-worktrees/ai-phase0-closeout；Cloud writer 独占 .codex-worktrees/cloud-cache-001；Edge writer先从最新 origin/main创建本交接指定新 worktree并独占。不要写默认 Cloud/AI main目录。

按 Wave 0→5执行。Cloud 最终 compatibility pin 必须等待 Edge 新 candidate commit，不得硬编码旧 Edge SHA。每仓 targeted red/green 后跑完整 required和质量门禁，更新复盘/正式规则，普通 commit/push；Edge新建 Draft PR，Cloud更新#36，AI更新 stacked #60。保存首次失败，不用 rerun掩盖。

三仓最终 clean、PR CI绑定后，运行 contract digest、跨仓 duplication和Release非生产联合验收；生成14项机器 predicate catalog；把最终证据冻结到 evidence-only远端Git commit/Draft PR；再做独立严格终审。

当前正式状态是 REOPENED / NOT COMPLETE，不报22%或中间百分比。只有全部 blocking PASS才更新为14/14、100%。不执行任何生产操作。
```
