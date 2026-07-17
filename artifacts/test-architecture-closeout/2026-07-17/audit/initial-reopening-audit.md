# 三项目测试架构治理客观终审核查清单

> 文档版本：1.0  
> 审核快照：2026-07-16T23:18:52Z / 2026-07-17T07:18:52+08:00  
> 审核对象：EdgeClient、CloudPlatform、AICopilot 测试架构治理总计划  
> 主要复核者：Codex 主代理 + Edge/Cloud、AI、跨仓/总计划三路独立只读复核  
> 用途：交给 Kimi 或其他独立审核者复核；不得把本文的自然语言结论当成命令执行结果

## 1. 技术摘要：计划登记为 100%，独立证据终审为 FAIL

当前唯一活动总计划 `docs/三项目测试架构治理总计划.md` 登记为 **14/14、100%、已关单**。本次独立审核没有发现 Edge、Cloud、AI 已运行 required 测试数字被伪造，也没有发现引用的三个最终 Git HEAD、三个 PR、三个成功 run、coverage、AI mutation 或三仓联合验收数值互相冲突。

但是，按照本清单的零主观判定规则，当前不能确认“总计划已经全部处理完”。本次严格结论为：

| 口径 | PASS | FAIL | 结论 |
|---|---:|---:|---|
| 总计划文档登记 | 14 | 0 | 文档声明 100% |
| 本次严格 14 项证据审核 | 7 | 7 | **FAIL，不能确认客观关单** |

严格审核的确定性阻断如下：

1. `CLOUD-COMPAT-FINAL-HEAD-001`：Cloud compatibility baseline 和 PR workflow 固定的外部 Edge HEAD 是 `1d25f9a6945b0b73224cfc75250203ae35be17be`，不是最终 Edge HEAD `08eac58e3342571aef1759cd0188a79751c85d85`。对最终 Edge HEAD 执行现有门禁时命令退出码为 `1`。两个 Edge HEAD 之间只有两份文档变化，相关 consumer 源文件逐字节相同；这能证明语义源码未变，但不能把现行“精确 HEAD 相等”门禁的失败改写成 PASS。
2. `EDGE-DIECUT-ZERO-001`：总计划要求模切 UI、配置、测试和活动残留为零；当前活动 `edge-regression-ledger.json` 中仍有 `41` 个 `retired-diecut` 记录，且 `Test-EdgeRegressionLedger.ps1` 仍引用相关概念。总计划没有正式 allowlist 把这两个活动测试治理文件归类为允许保留的“非现行历史”，因此按字面零命中谓词为 FAIL。
3. `CLOSURE-PREDICATE-001`：14 项完成矩阵中的第 1、5、8、9、10、13 项没有封闭的机器谓词、输入集合、allowlist/denylist 或逐项证据索引。已有 CI 绿和文字说明不能机械证明“全部活动引用”“全部重复 helper”“全部高风险语义”“全部真实失效层”“稳定”“全部长期规则”这类全称命题。
4. `ROOT-EVIDENCE-IMMUTABILITY-001`：工作区根目录不是 Git 仓库；总计划和三份跨仓 JSON 是可变本地文件。当前 SHA-256 可以复算，但没有 Git object、远端 artifact manifest 或签名把它们冻结为不可替换证据。

这四项中任一项存在，整体结果都必须保持 FAIL。不得用加权分、平均分、主观“基本完成”或“实现应该没问题”覆盖。

## 2. 判定模型

### 2.1 唯一允许的状态

| 状态 | 机械定义 |
|---|---|
| `PASS` | 审核对象、输入 SHA、命令和期望值均已冻结；命令已执行；退出码为 0；全部必需谓词为 true |
| `FAIL` | 任一必需谓词为 false，或必需证据缺失/未绑定目标 HEAD，或完成谓词/阈值没有定义到可执行程度 |
| `NOT-RUN` | 谓词和命令已定义，但本次没有执行；不得换算成 PASS |
| `N/A` | 总计划明确排除，且该项不是当前自动化关单条件 |

### 2.2 FAIL 原因码

| 原因码 | 含义 |
|---|---|
| `PREDICATE_FALSE` | 已执行谓词，结果为 false |
| `BINDING_MISMATCH` | 证据绑定的 HEAD/tree/run/artifact 不是审核目标 |
| `EVIDENCE_MISSING` | 需要的原始证据、hash、日志、TRX 或封闭清单不存在 |
| `PREDICATE_UNDEFINED` | 使用了“全部、稳定、真实、重复、接近”等词，但没有封闭集合或数值阈值 |
| `EVIDENCE_MUTABLE` | 证据只有可变本地副本，没有不可变锚点或签名 |
| `COMMAND_FAILED` | 复核命令退出码非 0 |

### 2.3 总体判定公式

```text
总体 PASS = 所有 blocking=true 的检查项状态均为 PASS
总体 FAIL = 任一 blocking=true 的检查项状态为 FAIL 或 NOT-RUN
N/A 不进入分母
禁止 partial pass、conditional pass、加权折算和人工印象分
```

### 2.4 证据优先级

1. Git object、GitHub PR/run/check/artifact 元数据及其不可变 SHA/digest。
2. 已提交且绑定目标 HEAD 的机器 JSON、TRX、coverage/mutation 原始报告。
3. 当前可复算 SHA 的工作区根证据；必须同时披露其可变性。
4. 计划、复盘、交接或评论中的自然语言，只能提供索引，不能单独证明 PASS。

## 3. 审核范围与禁止扩展

### 3.1 唯一权威入口

- 工作区规则：`AGENTS.md`
- 总规则：`docs/总规则.md`
- 唯一活动计划：`docs/三项目测试架构治理总计划.md`
- 测试证据入口：`docs/testing/README.md`
- Edge 规则：`IIoT.EdgeClient/docs/客户端规则.md`
- Cloud 规则：`IIoT.CloudPlatform/docs/云端规则.md`
- AI 规则：`AICopilot/AGENTS.md`、`AICopilot/资料/AICopilot业务规则.md`

### 3.2 禁止作为执行入口

`docs/三项目源码审计与架构治理执行计划.md` 是归档计划，本审核禁止把它作为当前范围、进度或完成条件来源。

### 3.3 明确不在本轮自动化关单范围

- PR merge、mainline adoption、生产发布和生产部署。
- Windows 现场实机、真实 PLC/MES/Cloud/模型、LiveExternal、现场安装升级。
- Edge 宿主仓与插件仓拆分。
- GitHub Organization/Enterprise、额外 reviewer 或自定义授权链建设。

这些项目状态应为 `N/A`，不得写成“已执行”或“已通过”。

## 4. 冻结审核快照

### 4.1 权威 worktree、分支、HEAD 和 tree

| 项目 | 权威 worktree | 分支 | HEAD | tree | clean |
|---|---|---|---|---|---|
| Edge | `IIoT.EdgeClient` | `agent/edge-test-architecture-20260714` | `08eac58e3342571aef1759cd0188a79751c85d85` | `4aafe618a72a4d6161931e144a133664761fcac2` | true |
| Cloud | `.codex-worktrees/cloud-cache-001` | `agent/cloud-test-architecture-20260714` | `ef7d950e83b7c119c173e4b800846bffb43f7e95` | `a8ed1fcae6c1be7f527e9de98343c72095d0a2d7` | true |
| AI | `.codex-worktrees/ai-phase0-closeout` | `agent/ai-test-architecture-20260714` | `87b2336630125d6168b0b7efb5d4b4e8a97a2c60` | `4a8fa2ce99d7157791e02d197a04a0ca5cb56701` | true |

默认目录 `IIoT.CloudPlatform` 和 `AICopilot` 不是本计划最终候选 worktree，当前分别位于旧 HEAD 且包含其他脏改动。任何复核命令若省略显式 root，都会审核错误对象。

### 4.2 PR、base、run 和 checks

| 项目 | PR | base | 状态 | final run | 结果 |
|---|---|---|---|---:|---|
| Edge | `ShuJinHao/IIoT.EdgeClient#52` | `main` | OPEN / Draft / CLEAN | `29495673942` | required checks SUCCESS |
| Cloud | `ShuJinHao/IIoT.CloudPlatform#36` | `main` | OPEN / Draft / CLEAN | `29495656348` | required `build-test` SUCCESS；两个 optional job SKIPPED |
| AI | `ShuJinHao/AICopilot#60` | `agent/ai-workflow-branch-failures@198cc59318f4a1748c719b9b8ecff1d969952ce8` | OPEN / Draft / CLEAN | `29517763217` | 5/5 checks SUCCESS |

AI PR #60 是 stacked Draft PR，不是直接面向 `main`。其 Actions checkout 为 synthetic merge `ad4d8ac0ccb56b610cb4b410f1390e82ed1d5b8a`；独立抓取该 merge ref 得到 tree `4a8fa2ce99d7157791e02d197a04a0ca5cb56701`，与候选 HEAD tree 完全相等，parents 为 base `198cc593...` 和 head `87b233...`。

### 4.3 关键证据 SHA-256

| 文件/Artifact | SHA-256 / digest |
|---|---|
| 活动总计划 | `913da58a8d487c93645d1800eb2b995aab21cc939f73648d7a8fca30335482ef` |
| 终审机器证据清单 | `e20b3c5223b0dabb1f1d7a04d2abdc0dbbeedcd6114b5e5e101c2bc088130d7b` |
| 根 contract digest JSON | `098142ac6af6151b3006812412b920cbe1447d4dd14ee30398d8290901d02b4a` |
| 根 cross-repository duplication JSON | `0717bff0b0512fd71c90b29ab50b2bd8e7f33df62cd920099a5ac90f4f6b7288` |
| 权威 non-production joint acceptance JSON | `e15c88a56a331d7f77a98904475f1b14292d6d9f1de178faf5116e304cd75cd7` |
| 交接文档 | `9fc79526a647669f6ebd632db6123c3313b62084e5aef5b6042744d019d8c654` |
| Edge required artifact `8374750466` | `sha256:0213a25bcd47009f99b8639dc21ea28c6321c857e5c7b2dd0ba4cf713b1533d0` |
| Edge mutation artifact `8374298897` | `sha256:a20958c53ec1dcea6e648ee8d7ef551b577514f5f0793ef286fdeb77e42779c5` |
| Cloud required artifact `8374549175` | `sha256:667c708dd1f36c7e3ee212e4c2d492d91049351fa8151e6e979dd227707797ad` |
| AI unified required artifact `8383984370` | `sha256:556448d57e0e2a11a7ce914d3e5015641c40f7dd7126bcf54e95ebfa5a5029db` |
| AI final raw log | `1f2b4d446632b0a1f880b4bf97f38a81939b746e0856288cfcd4bcf0fe1bb385` |

根 contract JSON 与联合验收目录中的 contract JSON 文件 hash 不相同，因为 `generatedAtUtc` 不同；两者的 contract digest、脚本 SHA、provider/consumer HEAD 和语义字段一致。不得错误要求两份完整 JSON 逐字节相同。

## 5. 14 项 AND 完成条件严格审核

| # | blocking | 可执行完成谓词 | 当前状态 | 原因码 | 客观依据 |
|---:|---|---|---|---|---|
| 1 | true | 冻结错误治理资产 denylist + 允许保留位置 allowlist；三仓活动文件零命中 | `FAIL` | `PREDICATE_UNDEFINED` | 当前无封闭资产清单和零命中报告；只能证明 CODEOWNERS 等已知文件不存在 |
| 2 | true | 模切源码/UI/配置/测试/solution/打包输入/活动引用零命中，生成目录零个 | `FAIL` | `PREDICATE_FALSE` | 生成目录为 0；但活动 regression ledger 有 41 个 `retired-diecut` 记录，且无正式 allowlist |
| 3 | true | Edge 宿主/SDK 只使用中性 TestPlugin 和通用 lifecycle/completion/cancellation seam | `PASS` | — | tracked 内容存在并进入 Edge required 成功 run |
| 4 | true | 三仓 inventory 中每个 case 唯一、分类完整、无未决分类、required/manual 对账相等 | `PASS` | — | Edge 32/1280；Cloud 18/725；AI 25/1025，静态 JSON 对账通过 |
| 5 | true | 冻结旧桶/filter/helper denylist；活动源码、workflow、project graph 零命中 | `FAIL` | `PREDICATE_UNDEFINED` | 旧项目路径和已知 filter 可证为零，但“重复 helper”的全集和 allowlist 未定义 |
| 6 | true | required discovered=executed=passed，failed=0，skipped=0，run head=候选 head | `PASS` | — | Edge 1280、Cloud 661、AI 1011 全部满足；job skip 与 case skip 已分离 |
| 7 | true | 三仓 Analyzer/graph/static negative fixtures 必须失败，positive fixtures 必须通过，并进入 required run | `PASS` | — | 三仓 workflow 和成功 run 含 analyzer、AnalyzerTests、真实违规 fixture |
| 8 | true | 冻结高风险语义 identity 矩阵；每个 identity 绑定测试、runner、结果和 HEAD | `FAIL` | `EVIDENCE_MISSING` | 有大量对应测试，但没有封闭的三仓 identity→test→result/hash 清单，无法证明“全部” |
| 9 | true | TestKind/Runtime/ProjectReference/Capability 的允许矩阵可执行，违规 fixture 必须失败 | `FAIL` | `PREDICATE_UNDEFINED` | inventory 能证明分类存在，不能机械证明每项均处于“真实失效层” |
| 10 | true | duplication/coverage/mutation/compatibility 各有冻结 baseline、阈值、最终 HEAD 绑定且门禁退出 0 | `FAIL` | `BINDING_MISMATCH` | Cloud compatibility 绑定旧 Edge HEAD；对最终 Edge HEAD 重跑退出 1 |
| 11 | true | nearest-rank p95 ≤ 1500 秒；样本和 run IDs 明示；required 数量与 skip 不减少 | `PASS` | — | Edge 1283s n=2；Cloud 739s n=1；AI 1382s n=3；均 ≤1500s。样本小只限制外推，不改变算术结果 |
| 12 | true | Provider→Consumer→joint 串行；三仓 clean HEAD/tree 绑定；Release；noBuild=false；0 fail/skip；无生产操作 | `PASS` | — | digest、TRX、identity marker 和联合 JSON 可独立复算 |
| 13 | true | 所有长期规则有唯一 rule catalog，映射正式规则位置和自动门禁；复盘不作为唯一来源 | `FAIL` | `EVIDENCE_MISSING` | 有正式规则文件，但不存在封闭 rule→formal location→gate catalog |
| 14 | true | 未执行项被明确列出；PR 未 merge；引用 CI 无生产 deploy 入口 | `PASS` | — | 三 PR Draft/Open；workflow 权限与日志无生产部署；不能外推为仓外从未有人操作 |

严格结果：`PASS=7`、`FAIL=7`。整体为 `FAIL`。

## 6. EdgeClient 审核清单

| ID | blocking | 精确判据 | 当前状态 | 证据/限制 |
|---|---|---|---|---|
| EDGE-GIT-001 | true | 权威 worktree clean，HEAD/tree/remote branch 等于冻结值 | `PASS` | 本地 Git 与 PR #52 一致 |
| EDGE-PR-001 | true | PR #52 OPEN、Draft、head 正确、required checks SUCCESS | `PASS` | smoke-build、mutation-report 均 SUCCESS |
| EDGE-INV-001 | true | required runner=32，case=1280，case identity 唯一 | `PASS` | inventory 机器 JSON |
| EDGE-TEST-001 | true | discovered=executed=passed=1280；failed=skipped=0 | `PASS` | run `29495673942` required artifact |
| EDGE-COVERAGE-001 | true | 32 reports；line≥baseline；branch≥baseline；高风险 coverage gate 通过 | `PASS` | actual line `0.657581`、branch `0.512107` |
| EDGE-ANALYZER-001 | true | Analyzer、AnalyzerTests、正反 project graph fixtures 进入 required run | `PASS` | smoke-build 成功 |
| EDGE-MUTATION-001 | true | 报告范围、mutant 分母和状态可复算；不把局部结果泛化为全仓 | `PASS` | 251 total；68 killed、84 survived、45 noCoverage、53 ignored、1 compileError；score `34.5178%`，report-only |
| EDGE-DUP-001 | true | 仓内 baseline/ratchet 无新增违规 | `PASS` | required evidence 通过 |
| EDGE-PLUGIN-001 | true | 中性 TestPlugin 覆盖通用插件生命周期/取消/完成边界 | `PASS` | tracked test + successful run |
| EDGE-STARTUP-001 | true | Startup 非阻断矩阵进入自动化门禁 | `PASS` | required run 通过 |
| EDGE-OUTBOUND-001 | true | PLC/MES/Cloud probe/gate/retry/fallback/deadletter 分离和副作用次数测试通过 | `PASS` | required run 通过；完整 identity catalog 仍缺失于总计划层 |
| EDGE-DIECUT-001 | true | 活动测试治理资产对模切关键字零命中，除正式 allowlist | `FAIL` | 41 个 `retired-diecut`；allowlist 未定义 |
| EDGE-PURGE-001 | true | `.vs/bin/obj/artifacts/.artifacts/publish/staging` 目录数=0 | `PASS` | 本次 `find` 结果为 0 |
| EDGE-LOCAL-RERUN-001 | false | 在最终 HEAD 本地重新 build/test 后仍复现 1280/1280 | `NOT-RUN` | 为保持最终 purge 状态，本次没有重跑 Edge build/test；使用不可变 CI artifact |
| EDGE-WINDOWS-001 | false | Windows 实机/现场设备验收 | `N/A` | 总计划明确排除 |

## 7. CloudPlatform 审核清单

| ID | blocking | 精确判据 | 当前状态 | 证据/限制 |
|---|---|---|---|---|
| CLOUD-GIT-001 | true | 权威 worktree clean，HEAD/tree/remote branch 等于冻结值 | `PASS` | 必须使用 `.codex-worktrees/cloud-cache-001` |
| CLOUD-PR-001 | true | PR #36 OPEN、Draft、head 正确、required build-test SUCCESS | `PASS` | run `29495656348` |
| CLOUD-INV-001 | true | inventory=18 runners/725 cases；required=16/661 | `PASS` | JSON 对账通过 |
| CLOUD-TEST-001 | true | required 661/661，failed=skipped=0 | `PASS` | required artifact |
| CLOUD-WEB-001 | true | Vitest 67 + Browser Smoke 2 = 69；failed=skipped=0 | `PASS` | final required run |
| CLOUD-DEPLOY-BEHAVIOR-001 | true | 非生产 deployment behavior 33/33，failed=skipped=0 | `PASS` | final required run；不是生产部署 |
| CLOUD-COVERAGE-001 | true | 16 reports；line/branch 和 P0 new-code thresholds 通过 | `PASS` | line `11677/18488=0.63159887`；branch `2815/5619=0.50097882` |
| CLOUD-ANALYZER-001 | true | Analyzer/graph/正反 fixture 进入 required run | `PASS` | valid 8、invalid 15、callGraph bypass 20、suppression bypass 3 均受门禁 |
| CLOUD-CACHE-001 | true | cache、取消、factory、安全缓存语义测试进入 required run | `PASS` | required run 通过 |
| CLOUD-DUP-001 | true | 六类仓内 duplication ratchet 通过 | `PASS` | evidence 通过 |
| CLOUD-COMPAT-001 | true | external evidence HEAD=最终 Edge HEAD，source hash/consumer pattern 匹配，命令退出 0 | `FAIL` | baseline/workflow 固定 `1d25f9a...`；最终为 `08eac58e...`；重跑退出 1 |
| CLOUD-COMPAT-SOURCE-001 | false | 旧/新 Edge HEAD 之间相关 consumer 源文件无差异 | `PASS` | 两 HEAD 只差两份 docs；CapacitySyncTask、DeviceLogSyncTask 无 diff |
| CLOUD-E2E-FINAL-001 | false | 最终 Cloud SHA 的 `full-end-to-end` job 实际执行 | `NOT-RUN` | job 为 SKIPPED；63 E2E 来自较早固化证据；最终提交仅改 docs |
| CLOUD-MUTATION-FINAL-001 | false | 最终 Cloud SHA 的 mutation job 实际执行 | `NOT-RUN` | job 为 SKIPPED；21/21 killed、100% 来自既有 cadence evidence；不能写成 final run 已执行 |
| CLOUD-JOB-SKIP-LABEL-001 | true | 文档明确区分 CI job SKIPPED 与 required test case skipped=0 | `PASS` | 两种口径不得混写 |
| CLOUD-LIVE-001 | false | 真实生产/LiveExternal/现场链路 | `N/A` | 总计划明确排除 |

Cloud compatibility 复现命令：

```bash
pwsh -NoProfile -File .codex-worktrees/cloud-cache-001/scripts/tests/Test-CloudCompatibility.ps1 \
  -ReportDirectory /private/tmp/cloud-final-compatibility-audit \
  -EdgeRepositoryRoot /Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient \
  -BaseRef 88c41109fbcf0b87b18939a139e0bff751e03d07
```

当前客观输出摘要：

```text
External consumer evidence HEAD changed:
baseline=1d25f9a6945b0b73224cfc75250203ae35be17be
actual=08eac58e3342571aef1759cd0188a79751c85d85
item=upload-content-hash-idempotency
exit=1
```

## 8. AICopilot 审核清单

| ID | blocking | 精确判据 | 当前状态 | 证据/限制 |
|---|---|---|---|---|
| AI-GIT-001 | true | 权威 worktree clean，HEAD/tree/upstream 相等，ahead/behind=0/0 | `PASS` | `.codex-worktrees/ai-phase0-closeout` |
| AI-PR-001 | true | PR #60 OPEN、Draft、CLEAN，base/head 完整记录 | `PASS` | stacked base `198cc593...`，不是 main |
| AI-MERGE-TREE-001 | true | synthetic merge tree=候选 tree | `PASS` | `ad4d8ac...` tree=`4a8fa2ce...` |
| AI-CHECKS-001 | true | final run attempt 1，5/5 checks SUCCESS | `PASS` | run `29517763217` |
| AI-ANNOTATION-001 | true | 5 个 GitHub check annotations_count=0 | `PASS` | 只能表述为 GitHub annotations=0 |
| AI-INV-001 | true | 25 projects/1025 cases=1011 required+14 Manual；case identity 唯一 | `PASS` | 14 是 case 数，不是项目数 |
| AI-DOTNET-001 | true | 17 required runners，1011/1011，failed=skipped=0 | `PASS` | unified artifact |
| AI-VITEST-001 | true | 165/165，failed/pending/todo=0 | `PASS` | 28 files / 56 suites |
| AI-PLAYWRIGHT-001 | true | 43/43，unexpected/skipped/flaky=0 | `PASS` | 最终 Chromium 环境修复后的 run |
| AI-DEPLOY-BEHAVIOR-001 | true | 33/33，`productionEligible=false` | `PASS` | 只证明非生产机制测试 |
| AI-COVERAGE-001 | true | 17 logical、28 assemblies；分子/分母与 baseline 精确一致 | `PASS` | line `71454/87937=0.81255899`；branch `8399/15581=0.53905398` |
| AI-MUTATION-001 | true | 选定文件 active/evaluated=58、killed=58、survived=noCoverage=0 | `PASS` | 73 generated、14 ignored、1 compileError；100% 只适用于 SecretStringEncryptor 范围 |
| AI-COMPAT-001 | true | active compatibility 项全部有 consumer、期限、删除条件、测试；unclassified=0 | `PASS` | 仍有 4 个活动项，不是兼容代码清零 |
| AI-DUP-001 | true | duplication ratchet 通过 | `PASS` | duplication 数量非零，不得写成全仓无重复 |
| AI-ANALYZER-001 | true | AIARCH001-007 为 Error/NotConfigurable；AnalyzerTests 和真实 fixture 全绿 | `PASS` | AnalyzerTests 30，real-project fixture 28 |
| AI-ARTIFACT-FANIN-001 | true | 4 个 producer 的 79 文件与 unified artifact 逐字节一致；missing/mismatch=0 | `PASS` | unified artifact 额外 1 个 required summary |
| AI-LOG-WARNING-001 | true | 只声明 annotations=0，不声明完整日志零 warning | `PASS` | raw log 有 node_modules Rolldown 文本和 Node deprecation warning |
| AI-RETRO-TRACE-001 | false | AI 最终滚动复盘回写 final run 29517763217 | `NOT-RUN` | final commit 复盘仍写“需再跑 CI”；闭环在根总计划和 GitHub 证据 |
| AI-MANUAL-001 | false | 14 Manual/LiveExternal cases 在 required CI 执行 | `N/A` | 总计划明确排除 |
| AI-MAINLINE-001 | false | PR 合入 main 并在新 base 上重验 | `N/A` | 本计划未 merge；base 移动后必须重跑 |

## 9. 跨仓证据审核清单

| ID | blocking | 精确判据 | 当前状态 | 客观结果 |
|---|---|---|---|---|
| CROSS-DIGEST-001 | true | contract id=`edge-pass-station-batch-v1` | `PASS` | 固定值一致 |
| CROSS-DIGEST-002 | true | Edge/Cloud snapshot 原字节 SHA 相等 | `PASS` | 两边均为 `86cc7ca5399d6af3524ce34f2cace3ea926625b808b72f30f83b3118b8fa6d81` |
| CROSS-DIGEST-003 | true | provider/consumer HEAD 等于最终冻结 HEAD | `PASS` | Cloud `ef7d...` / Edge `08ea...` |
| CROSS-DIGEST-004 | true | digest script SHA 等于当前脚本 SHA | `PASS` | `4928ad9278a8de710814f65fe9432cc65273545ff25b63baa140a8d2c40077f3` |
| CROSS-DUP-001 | true | 3 类×2 配置=6 个扫描组合完整唯一 | `PASS` | 六组均存在 |
| CROSS-DUP-002 | true | production/support/test-case 文件数精确对账 | `PASS` | 2252 / 51 / 431 |
| CROSS-DUP-003 | true | 每组 summary count=raw array length；总跨仓组=81 | `PASS` | 独立 jq 对账通过 |
| CROSS-DUP-004 | true | duplication script SHA 等于当前脚本 SHA | `PASS` | `27bcdc936b7b24bbfbb62e8f985f2499da084757daba572d17fe46d749bb7811` |
| CROSS-DUP-RAW-HASH-001 | true | 六份 raw jscpd 文件均有独立 hash manifest | `PASS` | 终审机器证据清单已保存六份逐文件 SHA-256，并经 `mismatches=0` 复核 |
| JOINT-001 | true | `authoritative=true`、Release、`noBuild=false` | `PASS` | 三项均满足 |
| JOINT-002 | true | 三仓最终 HEAD/tree clean 且前后不变 | `PASS` | JSON 绑定三仓最终候选 |
| JOINT-003 | true | provider 47/47、consumer 87/87、alignment 1/1；0 fail/skip | `PASS` | TRX 独立对账通过 |
| JOINT-004 | true | identity marker 可复算 | `PASS` | `bee8f7fda293c1d35734b77ed701d2aca24639a8ea0fbad0074b541c3c993123` |
| JOINT-005 | true | `productionOperations=false`、`deployChangedInvoked=false` | `PASS` | 只能证明该入口及已检查 workflow 范围 |
| JOINT-006 | true | joint script SHA 等于当前脚本 SHA | `PASS` | `e4dd4593f15879dcaa4faeacfc50a30ba8f684e09a2454ce712e92dbdb173da5` |
| ROOT-IMMUTABLE-001 | true | 根计划和三份 JSON 有 Git/远端 artifact/signature 锚点 | `FAIL` | `EVIDENCE_MUTABLE`：工作区根非 Git 仓库 |

联合验收生成时间为 `2026-07-16T17:26:42.2714500Z`，换算 Asia/Shanghai 为 `2026-07-17T01:26:42.2714500+08:00`。不得只写“2026-07-16”而隐藏时区差异。

## 10. GitHub、CI 和 Artifact 审核清单

| ID | blocking | 判据 | 当前状态 | 备注 |
|---|---|---|---|---|
| GH-HEAD-001 | true | 三 PR headRefOid 等于冻结 HEAD | `PASS` | 三仓均一致 |
| GH-RUN-001 | true | 三 final run event=pull_request、attempt=1、completed/success | `PASS` | 三仓均一致 |
| GH-REQUIRED-001 | true | 所有 required checks SUCCESS | `PASS` | Cloud optional skip 不属于 required case skip |
| GH-DRAFT-001 | true | 三 PR 均 OPEN/Draft、未 merge | `PASS` | 不代表 mainline 完成 |
| GH-ARTIFACT-DIGEST-001 | true | 下载 artifact digest 与 GitHub 元数据相等 | `PASS` | 关键 digest 见 4.3 |
| GH-ARTIFACT-RETENTION-001 | true | 审核报告记录 artifact 到期风险 | `PASS` | Edge required 预计 2026-07-30；Cloud 预计 2026-10-14；AI producer 1 天、unified 约 90 天 |
| GH-DEPLOYMENTS-001 | false | GitHub deployments API 返回完整历史并证明零部署 | `NOT-RUN` | 查询时 API 503；且该 API 也不能排除所有仓外手工通道 |
| GH-NO-PROD-001 | true | 引用 workflow 权限/命令/日志不含生产 deploy | `PASS` | 只对引用 run 有效 |

## 11. 统计与指标口径

### 11.1 required 测试

| 项目 | discovered | executed | passed | failed | skipped | 状态 |
|---|---:|---:|---:|---:|---:|---|
| Edge | 1280 | 1280 | 1280 | 0 | 0 | `PASS` |
| Cloud | 661 | 661 | 661 | 0 | 0 | `PASS` |
| AI .NET | 1011 | 1011 | 1011 | 0 | 0 | `PASS` |

Cloud GitHub 的 `full-end-to-end` 和 `mutation-report` job 是 SKIPPED；这与 Cloud required test case `skipped=0` 是不同分母，禁止混写为“所有 CI 均 0 skipped”。

### 11.2 coverage

| 项目 | line | branch | 结论边界 |
|---|---:|---:|---|
| Edge | 65.7581% | 51.2107% | 32 reports，当前值超过 baseline |
| Cloud | 63.159887% | 50.097882% | 16 required reports；P0 new-code gate 通过 |
| AI | 81.255899% | 53.905398% | 17 logical reports、28 assemblies，与 schema-v2 baseline 精确一致 |

不得用 coverage 百分比替代架构、行为、契约、异常或副作用语义测试。

### 11.3 mutation

| 项目 | 作用域 | 结果 | 禁止泛化 |
|---|---|---|---|
| Edge | committed report-only baseline | 68 killed / 84 survived / 45 noCoverage；score 34.5178% | 不是 100% |
| Cloud | cadence evidence | 21 evaluated / 21 killed；score 100% | final PR optional job 未执行 |
| AI | `SecretStringEncryptor.cs` 选定范围 | 58 evaluated / 58 killed；score 100% | 不是全仓 mutation 100% |

### 11.4 p95

nearest-rank 公式：排序后取 `ceil(0.95*n)` 的样本，索引从 1 开始。

| 项目 | 样本数 | p95 | 秒 | ≤1500 秒 |
|---|---:|---:|---:|---|
| Edge | 2 | 21m23 | 1283 | true |
| Cloud | 1 | 12m19 | 739 | true |
| AI | 3 | 23m02 | 1382 | true |

算术门槛为 PASS；样本数 `1/2/3` 不足以证明长期分布稳定。因为原计划没有预先定义最小样本数，本审核不事后发明样本阈值，但必须禁止“长期稳定”外推。

## 12. 可复制复核命令

### 12.1 Git 快照

```bash
git -C IIoT.EdgeClient status --porcelain=v1 --untracked-files=all
git -C IIoT.EdgeClient rev-parse HEAD 'HEAD^{tree}' origin/agent/edge-test-architecture-20260714

git -C .codex-worktrees/cloud-cache-001 status --porcelain=v1 --untracked-files=all
git -C .codex-worktrees/cloud-cache-001 rev-parse HEAD 'HEAD^{tree}' origin/agent/cloud-test-architecture-20260714

git -C .codex-worktrees/ai-phase0-closeout status --porcelain=v1 --untracked-files=all
git -C .codex-worktrees/ai-phase0-closeout rev-parse HEAD 'HEAD^{tree}' origin/agent/ai-test-architecture-20260714
```

三条 `status` 必须无输出；每个 HEAD/tree/remote branch 必须与 4.1 完全相等。

### 12.2 PR 与 CI

```bash
gh pr view 52 --repo ShuJinHao/IIoT.EdgeClient --json state,isDraft,mergeable,mergeStateStatus,headRefOid,statusCheckRollup
gh pr view 36 --repo ShuJinHao/IIoT.CloudPlatform --json state,isDraft,mergeable,mergeStateStatus,headRefOid,statusCheckRollup
gh pr view 60 --repo ShuJinHao/AICopilot --json state,isDraft,mergeable,mergeStateStatus,headRefOid,baseRefOid,statusCheckRollup

gh run view 29495673942 --repo ShuJinHao/IIoT.EdgeClient
gh run view 29495656348 --repo ShuJinHao/IIoT.CloudPlatform
gh run view 29517763217 --repo ShuJinHao/AICopilot
```

### 12.3 根证据 hash

```bash
shasum -a 256 \
  docs/三项目测试架构治理总计划.md \
  artifacts/testing/cross-repository-contract-digest.json \
  artifacts/testing/cross-repository-duplication/cross-repository-duplication.json \
  artifacts/testing/non-production-joint-acceptance/non-production-joint-acceptance.json \
  artifacts/handoffs/2026-07-16/三项目测试架构治理续跑交接-20260716-1445.md
```

### 12.4 联合验收字段

```bash
jq -e '
  .authoritative == true and
  .configuration == "Release" and
  .noBuild == false and
  .productionOperations == false and
  .deployChangedInvoked == false and
  ([.stages[] | select(.failed? != null) | .failed] | all(. == 0)) and
  ([.stages[] | select(.skipped? != null) | .skipped] | all(. == 0))
' artifacts/testing/non-production-joint-acceptance/non-production-joint-acceptance.json
```

### 12.5 Edge purge 和模切零残留

```bash
find IIoT.EdgeClient \
  -path '*/.git' -prune -o \
  -type d \( -name .vs -o -name bin -o -name obj -o -name artifacts -o -name .artifacts -o -name publish -o -name staging \) \
  -print

jq '[.. | objects | select(.disposition? == "retired-diecut")] | length' \
  IIoT.EdgeClient/scripts/tests/baselines/edge-regression-ledger.json

rg -l -i 'die.?cut|模切|retired-diecut' IIoT.EdgeClient \
  --glob '!docs/改动复盘与规则沉淀.md' \
  --glob '!docs/历史核心记录.md' \
  --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/.git/**'
```

当前预期：第一条无输出；第二条输出 `41`；第三条输出两个活动测试治理文件。因此“生成目录归零”PASS，“活动模切关键字零命中”FAIL。

### 12.6 跨仓 digest 安全复核

必须显式传最终 worktree；不得使用脚本默认 Cloud/AI 根目录。

```bash
pwsh -NoProfile -File scripts/testing/Test-CrossRepositoryContractDigest.ps1 \
  -EdgeRepositoryRoot /Users/shushu/Developer/产线系统架构升级/1/IIoT.EdgeClient \
  -CloudRepositoryRoot /Users/shushu/Developer/产线系统架构升级/1/.codex-worktrees/cloud-cache-001 \
  -OutputPath /private/tmp/cross-repository-contract-digest-audit.json
```

### 12.7 全量联合复验的副作用警告

`Invoke-ThreeRepositoryNonProductionAcceptance.ps1` 会重新 build/test Edge，并重新生成 `bin/obj`。如果执行，必须在审核记录中把 `EDGE-PURGE-001` 暂时改为 FAIL，完成复验后按 Edge 规则清理并再次扫描。不得一边重建生成目录，一边沿用旧的“目录为零”结论。

## 13. 证据保存与可复现性缺口

| ID | 缺口 | 当前状态 | 关闭条件 |
|---|---|---|---|
| GAP-001 | Cloud compatibility 未绑定最终 Edge HEAD | OPEN / blocking | 更新 baseline/workflow 到最终 HEAD并重跑，或正式改为 source-state hash 语义且添加相应 fixture；命令退出 0 |
| GAP-002 | 模切活动 regression ledger 无正式 allowlist | OPEN / blocking | 删除活动记录，或在正式规则中定义唯一允许历史位置并让扫描只命中 allowlist |
| GAP-003 | 完成项 1/5/8/9/10/13 缺封闭谓词与 evidence index | OPEN / blocking | 建立机器 catalog，逐项绑定 HEAD、命令、hash、expected/actual |
| GAP-004 | 根 evidence 可变、未签名、未远端锚定 | OPEN / blocking | 将 manifest 提交到受审 Git 仓库或上传带 digest 的长期 artifact，并保存签名/attestation |
| GAP-005 | raw jscpd 六份报告此前没有逐文件 hash manifest | CLOSED | 终审机器证据清单已保存六份 SHA-256，当前复算 `mismatches=0`；仍受 GAP-004 的根级不可变性限制 |
| GAP-006 | Cloud final E2E/mutation optional jobs 未执行 | OPEN / non-blocking under current cadence | 如要求 final-SHA 全量闭环，手工触发并绑定该 SHA |
| GAP-007 | p95 样本量小 | OPEN / disclosure | 预先定义样本窗和最小 n 后累计；不得回改当前算术结果 |
| GAP-008 | artifact 有到期时间 | OPEN / disclosure | 在到期前复制到长期、只读、带 digest 的证据库 |

## 14. 关闭阻断项的唯一客观顺序

1. 冻结审核 catalog：为 14 项 AND 和所有批次 ID 定义 input set、allowlist/denylist、命令、expected、blocking、证据路径和 SHA。
2. 修复 Cloud compatibility 最终 HEAD 绑定，并在最终 Cloud/Edge 候选上让现有/新定义门禁退出 0。
3. 裁决 Edge regression ledger 的 41 个模切历史记录：删除，或在正式规则中定义可机器验证的唯一 allowlist；不得靠文字解释跳过。
4. 生成高风险语义、真实失效层、重复 helper、长期规则的封闭 catalog，并将每项映射到测试 identity 和 immutable result。
5. 将已生成的根 evidence/raw jscpd hash manifest 和三仓最终 snapshot 放入不可变远端证据，并补 attestation/signature。
6. 重跑受影响 required CI；若 base/head/tree 改变，重新生成 contract digest、duplication 和非生产联合验收。
7. 只有所有 blocking 项均为 PASS，才允许把独立审核结论改为 14/14 和 100%。

## 15. 给 Kimi 的复核指令

将本文件和其引用的证据一并提供给 Kimi，使用以下指令：

```text
你是独立证据审核者，不是总结者。

规则：
1. 只允许 PASS / FAIL / NOT-RUN / N/A。
2. PASS 必须有冻结输入、完整命令、退出码 0、expected=actual、且证据绑定目标 HEAD/tree/run/artifact。
3. 自然语言声明、已有“已完成”状态、CI 绿色图标、计划百分比都不能单独证明 PASS。
4. 任一必需谓词未定义、证据缺失、证据可变未锚定、HEAD 不一致或命令非 0，均判 FAIL，并给出原因码。
5. 不得给加权分、主观成熟度、‘基本通过’、‘大致完成’或‘建议忽略’。
6. 先核对禁止读取的归档计划，不能用它扩大范围或补完成条件。
7. 如果你拿不到本地 workspace/GitHub/artifact，所有需要执行的项必须标 NOT-RUN，不能从本文结论复制 PASS。
8. 输出顺序固定为：确定性反例 → 14 项矩阵 → 三仓明细 → 跨仓证据 → 未执行边界 → 总体结论。

请逐条复核本文件，并专门尝试推翻以下结论：
- Cloud compatibility 对最终 Edge HEAD 退出 1；
- Edge 活动 regression ledger 有 41 个 retired-diecut；
- 完成项 1/5/8/9/10/13 缺封闭机器谓词；
- 根 evidence 未被 Git/签名/长期 artifact 锚定。

只有你能用原始证据把上述每个阻断项改为 PASS，才允许判总计划客观完成。
```

## 16. 机器可读摘要

```json
{
  "schemaVersion": 1,
  "auditSnapshotUtc": "2026-07-16T23:18:52Z",
  "planRegisteredStatus": {
    "status": "PASS",
    "passed": 14,
    "failed": 0,
    "progressPercent": 100,
    "meaning": "document-registered"
  },
  "independentEvidenceAudit": {
    "status": "FAIL",
    "passed": 7,
    "failed": 7,
    "progressPercent": null,
    "reason": "AND audit does not permit weighted percentage"
  },
  "blockingFindingIds": [
    "CLOUD-COMPAT-FINAL-HEAD-001",
    "EDGE-DIECUT-ZERO-001",
    "CLOSURE-PREDICATE-001",
    "ROOT-EVIDENCE-IMMUTABILITY-001"
  ],
  "candidateHeads": {
    "edge": "08eac58e3342571aef1759cd0188a79751c85d85",
    "cloud": "ef7d950e83b7c119c173e4b800846bffb43f7e95",
    "ai": "87b2336630125d6168b0b7efb5d4b4e8a97a2c60"
  },
  "productionDeploymentExecutedByAuditedRuns": false,
  "prsMerged": false
}
```

## 17. 审核签收模板

```text
审核者：
审核时间 UTC / Asia/Shanghai：
审核 workspace 或证据包 SHA：
GitHub 查询时间：
14 项 PASS / FAIL / NOT-RUN / N/A：
阻断项 IDs：
是否执行了会生成 Edge bin/obj 的命令：
是否重新清理并复核 Edge purge：
是否发现生产部署、merge 或 LiveExternal 证据：
总体结论：PASS / FAIL
```

本清单没有执行 merge、push、生产部署、现场操作，也没有修改三项目代码。审核期间没有重跑 Edge build/test，因此保持 Edge 生成目录为零。
