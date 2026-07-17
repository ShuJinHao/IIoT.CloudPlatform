# 三项目测试证据操作入口

> 本目录只说明三仓测试证据与跨仓验收的复验方式。测试分类、发现数和执行结果以候选提交上的仓内原生 inventory、TRX/JUnit 和 CI artifact 为准，不再维护跨仓启发式分类副本。

## 1. 权威边界

权威顺序如下：

1. `docs/总规则.md`、三个项目规则和命中模块的专题契约；
2. `docs/三项目测试架构治理总计划.md`；
3. 三仓各自的机器 inventory、required count、执行汇总和 CI workflow；
4. 本目录的跨仓 contract digest、duplication report 与非生产联合验收证据。

一个测试只能由其仓内原生 inventory 归入一个主要 `TestKind`。根目录不重新按文件名猜测分类，也不复制三仓基线数字。最终数字必须来自同一候选提交的真实 discovery 和执行证据；历史数字只存在于计划或复盘的 before 对账中。

## 2. 三仓原生证据

### EdgeClient

- 机器清单：`IIoT.EdgeClient/scripts/tests/edge-test-inventory.json`
- required 基线：`IIoT.EdgeClient/scripts/tests/required-test-counts.json`
- 项目图核验：`IIoT.EdgeClient/scripts/tests/Get-EdgeTestInventory.ps1`
- TRX 对账：`IIoT.EdgeClient/scripts/tests/Confirm-EdgeRequiredTestResults.ps1`
- Analyzer 负例：`IIoT.EdgeClient/scripts/tests/Test-EdgeArchitectureAnalyzerFixtures.ps1`

### CloudPlatform

- 后端清单：`IIoT.CloudPlatform/src/tests/cloud-test-inventory.json`
- discovery、执行与 TRX 对账：`IIoT.CloudPlatform/scripts/tests/Invoke-CloudTestInventory.ps1`
- 前端清单：`IIoT.CloudPlatform/src/ui/iiot-web/test-inventory.json`
- Vitest/Browser Smoke/真实 E2E 对账：`IIoT.CloudPlatform/scripts/tests/Test-CloudWebInventory.ps1`
- Analyzer 负例：`IIoT.CloudPlatform/scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh`

### AICopilot

- 项目、分类和 discovery 清单：`AICopilot/scripts/tests/Get-AICopilotTestInventory.ps1`
- required TRX、前端和 deployment 对账：`AICopilot/scripts/tests/Confirm-AICopilotRequiredTestResults.ps1`
- Analyzer/测试基础设施负例：`AICopilot/scripts/tests/Test-AICopilotTestInfrastructureBehavior.ps1`

仓内脚本必须同时拒绝旧大桶、旧 filter、`Compile Link` 复用、测试源 Skip、未登记 runner 和支持项目中的测试 case。Required 证据固定满足 `discovered = executed = passed`、`failed = 0`、`skipped = 0`。

## 3. 跨仓证据

### Edge → Cloud contract digest

先分别执行 Cloud provider ContractTests 和 Edge consumer Cloud.ContractTests，再运行：

```powershell
pwsh ./scripts/testing/Test-CrossRepositoryContractDigestBehavior.ps1
pwsh ./scripts/testing/Test-CrossRepositoryContractDigest.ps1
```

digest 要求 provider/consumer snapshot 原字节一致，并再次验证每层精确字段集合、route、DTO 字段、长度、时间、payload 和 Edge request-id/emitted-value 语义；双方同时加入未审字段也会失败。双方仓内测试还必须分别绑定各自真实 controller/DTO/validator 与 uploader/route/hash builder；相同 JSON 不能代替生产绑定测试。
脚本会在读取前后校验两仓 clean HEAD；脏工作树不会生成绑定旧 SHA 的“权威”证据。

### 跨仓 duplication

```powershell
pwsh ./scripts/testing/Test-CrossRepositoryDuplicationReport.ps1
pwsh ./scripts/testing/Report-CrossRepositoryDuplication.ps1
```

生产源码、测试 support 和测试 case 分开扫描 strict/mild clone。跨仓 clone 只报告，禁止自动抽成共享业务包；三仓各自的 duplication baseline/ratchet 才是 PR 增量门禁。
报告入口会在拷贝源集前和扫描后复核三仓 clean HEAD；任一仓脏或扫描期间改变都直接失败。

### 非生产联合验收

候选提交 clean 后运行：

```powershell
pwsh ./scripts/testing/Invoke-ThreeRepositoryNonProductionAcceptance.ps1 -Configuration Release
```

固定顺序为 Cloud provider → Edge consumer → Edge/Cloud digest → 本地 Cloud/AICopilot WorkspaceAlignment。最后一段只启动本地 Aspire/测试资源，让真实 AICopilot typed client 调用 Cloud provider；不执行发布、部署、`Deploy-Changed.ps1`、稳定版推送或生产数据操作。

入口始终要求三个候选仓都是 clean HEAD；`-NoBuild` 只允许本地快速复验，输出会标记 `authoritative=false`，不能作为关单证据。

联合入口对 WorkspaceAlignment 的成功判定必须按唯一 `CLOUD_TEST_RUNNER_OK` marker 的键值语义解析：assembly 必须精确为 `IIoT.CloudPlatform.WorkspaceAlignmentTests`，且 `discovered = total = executed = passed > 0`、`failed = skipped = 0`。marker 后续增加字段或调整字段顺序不得造成假失败；缺字段、重复 marker、数量不一致或非零失败/跳过仍必须 fail-closed。

## 4. 最终验收口径

- 三仓候选提交分别 build，Analyzer 负例和 AnalyzerTests 通过；
- 所有 required runner、Vitest、Browser Smoke 和声明为 required 的脚本 suite 完成发现/执行/Skip 对账；
- 真实 E2E、mutation、扩展 runtime 和 LiveExternal 只在其正式 cadence 调度，不用 Skip 假绿；
- quality 证据包含 compatibility、production/support/test-case duplication、coverage 和 mutation baseline；
- CI artifact 必须来自实际 PR head，读取 jobs、annotations、首次失败和最终结果；
- Windows 实机、现场、生产和 LiveExternal 未执行项必须明确列出，不能由本机或 mock 证据代替。

历史的 `generated/test-inventory.json`、`generated/test-discovery.json`、启发式 overrides/schema 和对应生成器已随物理分层退役。它们记录的是旧项目桶和迁移前发现数，不再是执行入口。

## 5. 14 项最终闭包唯一入口

最终关单只使用 `scripts/testing/baselines/three-repository-closure-predicates.json` 中恰好 14 个 `CLOSURE-01` 至 `CLOSURE-14` blocking predicate；不得另建平行清单或用自然语言“全部通过”替代封闭输入集。每项必须同时绑定三仓 clean HEAD/tree、Draft PR 的精确 head OID、全新 required run/job/artifact、证据文件 SHA-256、正式规则位置与自动门禁。任一项为 `FAIL` / `NOT-RUN`、证据漂移或候选仓不 clean 时，总体保持 `REOPENED / NOT COMPLETE`。

先运行行为负例，再在三仓候选完全冻结且联合证据生成后运行实际闭包校验：

```powershell
pwsh -NoProfile -File scripts/testing/Test-ThreeRepositoryClosurePredicatesBehavior.ps1

pwsh -NoProfile -File scripts/testing/Test-ThreeRepositoryClosurePredicates.ps1 `
  -CatalogPath scripts/testing/baselines/three-repository-closure-predicates.json `
  -OutputPath artifacts/testing/closure/three-repository-closure-result.json `
  -EdgeRepositoryRoot .codex-worktrees/edge-startup-exception-retirement-closure `
  -CloudRepositoryRoot .codex-worktrees/cloud-cache-001 `
  -AiRepositoryRoot .codex-worktrees/ai-phase0-closeout
```

行为门禁必须证明：缺失或重复 ID、空输入 universe、无解释 allowlist、证据/hash 漂移、候选 HEAD 不符、blocking 非 PASS、正式规则只存在于复盘/历史、未封闭全称断言都会非零退出。实际闭包校验只有在 14 项均为精确 `PASS` 时才生成 `artifacts/testing/closure/three-repository-closure-result.json`；生成结果不是证据 catalog 的自引用输入，其 catalog SHA 由结果单向记录。

根目录不是 Git 仓。最终 catalog/result、三仓 CI binding、contract digest、跨仓 duplication、Release 非生产联合验收和严格复审报告必须按总计划第 15.2 节冻结到 Cloud evidence-only 分支的 Draft PR；禁止 merge、tag、部署或触发生产工作流。
