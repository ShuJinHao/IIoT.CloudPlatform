# 三项目测试架构治理审计后重新关单严格复审

日期：2026-07-17  
审核口径：只允许 `PASS / FAIL / NOT-RUN / N/A`；任一 blocking 非 `PASS` 即不得关单。  
审核输入：evidence commit A `ba54735afedee843064dce739a3e1e1acd9c85a6`，tree `53f04364ca2994fac0163caebedae0e1d81d4a19`，manifest SHA-256 `761fb7b494d7bdc67a17ab126cb44311cb43b78f511811d501993c37297eaa1f`，Draft PR [CloudPlatform #37](https://github.com/ShuJinHao/IIoT.CloudPlatform/pull/37)；以及严格复审后追加的 catalog command/AI coverage 绑定加固。最终加固内容只允许以普通追加 commit 固化，不覆盖 A/B。

该审核只使用 commit A 中的 reopened 计划快照、机器 catalog/result、三仓候选与 GitHub 绑定、原始报告和跨仓证据；不使用待写的关单计划快照反向证明自身。

## 1. 候选与远端证据绑定

| 仓 | clean HEAD / tree | Draft PR | final required run | 结果 | 状态 |
|---|---|---|---|---|---|
| Edge | `7474b3a919d8e0058ac529fdda6e2d0cd720390d` / `7dffa8b378d37865f86599deac8ed8f88a55a42a` | #53，OPEN、Draft、未 merge | `29558380849`，attempt 1 | 1332/1332，0 failed，0 skipped；required/mutation jobs SUCCESS | `PASS` |
| Cloud | `c06c238516470f0efb81464b9e3b83bb20811191` / `eff4ce0c0fa009f8868a89bd1dfc63ea24f861d9` | #36，OPEN、Draft、未 merge | `29562419923`，attempt 1 | 661/661，0 failed，0 skipped；build-test SUCCESS | `PASS` |
| AI | `59b93f7145fbe2dc908aa35970566d25875b473c` / `009187b40bf2052d1bd9c268629cc52410f2eb76` | #60，OPEN、Draft、未 merge | `29562903935`，attempt 1 | 1012/1012，0 failed，0 skipped；5/5 jobs SUCCESS | `PASS` |

AI 的 GitHub `pull_request` checkout commit 为 synthetic merge `15f9e94d2ef44bd7e70711ffef198776c9699171`；其 tree 与候选 tree 同为 `009187b40bf2052d1bd9c268629cc52410f2eb76`。本审核分别记录 run head、checkout commit 和相同 tree，不把三者混写。

evidence commit A 自身的非生产 Cloud PR 检查也在首次运行成功：run `29566054879`、attempt 1、head SHA=`ba54735afedee843064dce739a3e1e1acd9c85a6`，`build-test` job `87838814284` SUCCESS、annotations=0；artifact `cloud-required-test-results` ID `8401321166`、digest `sha256:80fb381b43efa50a88dbd4190033ba6cb45485cbe4e1bdf4f635c105545c8c60`。该检查只证明 evidence PR 未破坏 Cloud required gate，不替代三仓候选各自的 final CI。

## 2. 14 项 blocking 谓词复审

| ID | 复审结论 | 原因码 | 反证后依据 |
|---|---|---|---|
| CLOSURE-01 | `PASS` | `RETIRED_GOVERNANCE_ASSET_DENYLIST_ZERO` | 三仓冻结 tracked-file 集合对 CODEOWNERS/治理授权资产 denylist 为零；只允许正式规则、负例与历史追溯文本。 |
| CLOSURE-02 | `PASS` | `EDGE_RETIRED_FEATURE_AND_GENERATED_OUTPUT_ZERO` | Edge 活动输入 unexpected=0；正式 allowlist 精确两文件；retired declaration=41；行为负例 8；受审可再生目录=0。 |
| CLOSURE-03 | `PASS` | `EDGE_NEUTRAL_TESTPLUGIN_SEAM_VERIFIED` | 中性 TestPlugin 生命周期、完成、取消契约进入 Edge required run。 |
| CLOSURE-04 | `PASS` | `NATIVE_INVENTORY_CLASSIFICATION_RECONCILED` | Edge 32/1332；Cloud 18/725、required 661；AI 25/1026、required 1012、manual 14；case identity 唯一且分类闭合。 |
| CLOSURE-05 | `PASS` | `LEGACY_BUCKET_FILTER_HELPER_DENYLIST_CLOSED` | 旧桶、Required 隐藏 filter/Skip/降级、Compile Link、support 含 case/无 consumer 均由原生 inventory 负例和 duplication ratchet fail-closed。 |
| CLOSURE-06 | `PASS` | `FINAL_REQUIRED_RUNS_EXACT_AND_HEAD_BOUND` | 三仓 final attempt 1 均满足 discovered=executed=passed，failed=skipped=0，run head=PR head=候选 HEAD。 |
| CLOSURE-07 | `PASS` | `ANALYZER_POSITIVE_NEGATIVE_FIXTURES_FAIL_CLOSED` | Cloud 9 个真实 Contracts identity、valid=8、invalid=15、callGraph bypass=20、suppression bypass=3；Edge/AI 对应正反 gate 也进入 required run。 |
| CLOSURE-08 | `PASS` | `HIGH_RISK_IDENTITY_MATRIX_FULLY_BOUND` | 10 个冻结高风险 identity 均具 test、runner、PASS result、run、HEAD；覆盖 Edge startup/plugin/PLC-MES-Cloud、Cloud cache/permission/transaction-outbox-http-event、AI auth/read-only/cancellation/compensation/RAG E2E。 |
| CLOSURE-09 | `PASS` | `REAL_FAILURE_LAYER_MATRIX_ENFORCED` | TestKind/Runtime/Capability/ProjectReference 闭合矩阵存在；Pure/InProcess 越界、分类 override、Compile Link、未登记 runner 等负例均非零。 |
| CLOSURE-10 | `PASS` | `QUALITY_BASELINES_THRESHOLDS_FINAL_HEAD_BOUND` | Edge coverage line/branch `0.658909/0.514546`；Cloud `0.63159887/0.50097882` 且 compatibility 绑定最终 Edge clean source state；AI mutation 58/58；无 baseline 放宽。 |
| CLOSURE-11 | `PASS` | `FINAL_P95_WITHIN_BUDGET_NO_TEST_REDUCTION` | 最终 run 样本 Edge 1284s、Cloud 788s、AI 1372s，nearest-rank p95=1372s≤1500s；required 数相对下界不减少，skip=0。 |
| CLOSURE-12 | `PASS` | `RELEASE_NON_PRODUCTION_JOINT_ACCEPTANCE_BOUND` | Cloud provider 47、Edge consumer 87、WorkspaceAlignment 1；Release、noBuild=false、failed=skipped=0、productionOperations=false、deployChangedInvoked=false。 |
| CLOSURE-13 | `PASS` | `FORMAL_RULE_TO_GATE_CATALOG_COMPLETE` | 唯一 14 项 catalog；每项 formal rule path 可解析且 gate 非空；行为 gate 拒绝历史/复盘唯一规则和九类结构错误。 |
| CLOSURE-14 | `PASS` | `EXTERNAL_AND_PRODUCTION_BOUNDARIES_PRESERVED` | 三候选 PR 均 Draft/Open/未 merge；Cloud 生产 workflow 仅 workflow_dispatch；未执行 merge、部署、stable、生产 schema/data、服务器、现场或 LiveExternal。 |

机器结果：`blocking=14`、`passed=14`、`failed=0`、`notRun=0`、`status=PASS`。catalog SHA-256 为 `a9f340d464389df5c2f1d36514949feefa0a30c6131d7e87d3bb06d2b4b35805`，result SHA-256 为 `edb0f44f4d502d49ec9847dc4a3b91b9694aaa4bc49a9c074328cff1d1598047`。26 条非构建 command 已逐条实际执行且退出 0，所有 `pwsh -File` 入口均解析到现存脚本；会重建 Edge 产物的既有 analyzer/behavior gate 不在清理后重跑，而由相同候选 HEAD 的 final CI artifact 绑定。

## 3. 主动推翻检查

| 待推翻主张 | 实际反证动作 | 结果 | 状态 |
|---|---|---|---|
| RAG E2E 只是 `[Fact]` 文本，未被发现/执行 | 从 commit A 的 AI inventory 精确筛出唯一 `RagMcpEndToEndTests.RagAndMcpSmoke_ShouldIndexDocument_SearchContent_AndLoadOnlyEnabledMcp`，其 kind=`EndToEnd`、runtime=`Aspire`、required=true；再与 required summary 的 EndToEnd 25/25 和总 required 1012/1012 交叉对账 | 找到 1 个唯一 case，真实 discovery/execution 均进入成功 artifact | `PASS` |
| encv1 可以新增第三 caller 绕过 | 复核 compatibility report 的 `AI-COMPAT-SECRET-ENCV1=2`；检查行为 gate 同时注入 IsLegacy caller growth、ReEncrypt caller growth、未知 countMode 和 threshold 放宽；绑定 final governance-gates SUCCESS | 两个公开 surface 共享逐 caller-member ratchet，第三 caller/漏 surface/放宽阈值均由负例拒绝 | `PASS` |
| Cloud Analyzer fixture 仍是自造 Contracts，不会对真实 Contracts 漂移 fail-closed | 复核 fixture script 对真实 `IIoT.Services.Contracts.csproj` ProjectReference、真实 build output metadata probe 和 9 个 exact identity；检查 missing/rename 时无法解析恰好一次；与 artifact 日志交叉对账 | `CONTRACTS_METADATA_BINDING_OK identities=9`，全部正反/bypass fixture 进入 final build-test | `PASS` |
| Edge 未知异常/OCE 仍被翻译；模切在 allowlist 外回流 | 检查 startup integration 中 unknown `IOException` 与 `OperationCanceledException` 的 `Assert.Same`，以及批准异常的诊断非阻断路径；复核 source guard 禁止 bare/unfiltered broad catch；重新检查 retirement evidence=1、declarations=41、allowed=2、unexpected=0、negative=8 | 未知/OCE 保持同实例传播；活动模切零回流 | `PASS` |
| Cloud compatibility 只看 HEAD，doc-only 或任意 src 漂移都可能误判 | 检查 schema v3 report 中 reference/evidence HEAD、clean tracked-source digest、逐 consumer aggregate digest；检查内建 doc-only HEAD 正例以及 tracked-src drift、consumer drift、dirty repo 三个反例 | doc-only HEAD 变化可通过；任意 tracked src/consumer/dirty 变化 fail-closed | `PASS` |
| 14 项 catalog 未封闭、命令只是描述句，或未绑定最终证据 | 独立执行 jq：ID 精确 CLOSURE-01..14、每项 blocking/PASS/closed、每项 3 repo+3 GitHub bindings；逐条重算所有去重 evidence SHA，并核对 result.catalogSha256；逐条执行 26 条非构建 command；解析所有 pwsh entrypoint；将 AI coverage JSON 直接加入 CLOSURE-10 evidence | `STRICT_CATALOG_RECOMPUTE_OK predicates=14 bindingsPerPredicate=3 evidenceHashes=all`；`EXECUTABLE_CATALOG_COMMANDS_OK nonBuildCommands=26`；`CATALOG_PWSH_ENTRYPOINTS_OK` | `PASS` |
| 根证据只是本地文件，远端无法复算 | `git ls-remote` 复核 evidence branch 指向 commit A；本地 tree 与 PR #37 head OID 对账；从 manifest 所在目录重算 41 个被列文件 | remote head、tree 和 manifest 全部一致；41/41 hash OK | `PASS` |

manifest 初次复算曾从仓根执行，因清单采用证据目录相对路径而报告文件不存在；切换到 manifest 所在目录后 41/41 全部 `OK`。这是复算命令工作目录纠正，不是证据 hash 漂移。

## 4. 额外阻断批次与边界

| 项目 | 状态 | 依据 |
|---|---|---|
| AI declaration transition | `PASS` | source 785、current 769、retained-self 633、replaced 150、retired-duplicate 0、retired-source-guard 2、unresolved 0；source commit/tree/hash 锚点未改写。 |
| AI encv1 surface ratchet | `PASS` | 当前调用 2，上限 2；final governance gate 和 caller-growth 负例通过。 |
| Cloud real Contracts binding | `PASS` | 9 identities；valid 8/invalid 15；required 661/661。 |
| Edge startup exception allowlist | `PASS` | 批准异常非阻断；未知/OCE 同实例传播；required 1332/1332。 |
| 三仓跨仓联合证据 | `PASS` | contract digest `86cc7ca5399d6af3524ce34f2cace3ea926625b808b72f30f83b3118b8fa6d81`；duplication 六组扫描；Release 联合验收 authoritative=true。 |
| Windows 实机、现场、真实 PLC/MES/Cloud/模型、LiveExternal | `N/A` | 明确排除且未执行；不得由自动化证据外推。 |
| PR merge、生产部署、stable、Deploy-Changed、生产 schema/data、服务器/Harbor/SSH | `N/A` | 未授权且未执行。 |

## 5. 严格结论

第 15.2 节全部 blocking 批次与 14 项完成谓词均为 `PASS`；三仓候选 clean，远端 required CI 与候选 HEAD 一致，commit A 已由远端 Git ref 和 Draft PR 锚定，manifest 可独立复算。严格复审无 blocking `FAIL` 或 `NOT-RUN`。

因此允许更新总计划当前矩阵为 **14/14、100%、已关单**，并生成 evidence commit B。该结论不授权 merge、部署、stable、生产或 LiveExternal 操作，也不启动 Edge 宿主/SDK/私有插件拆分 Phase 0。
