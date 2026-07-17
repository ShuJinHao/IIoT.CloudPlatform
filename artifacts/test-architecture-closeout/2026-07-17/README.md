# 三项目测试架构治理 evidence-only 锚点

本目录冻结 2026-07-17 审计后修复的 commit A 证据集。它只用于提供远端可达、可按 SHA-256 复算的 Git 对象，不是 Cloud 产品变更，不得合并、发布、部署或触发生产操作。

候选绑定：

- Edge：`7474b3a919d8e0058ac529fdda6e2d0cd720390d` / tree `7dffa8b378d37865f86599deac8ed8f88a55a42a` / Draft PR #53 / run `29558380849`
- Cloud：`c06c238516470f0efb81464b9e3b83bb20811191` / tree `eff4ce0c0fa009f8868a89bd1dfc63ea24f861d9` / Draft PR #36 / run `29562419923`
- AI：`59b93f7145fbe2dc908aa35970566d25875b473c` / tree `009187b40bf2052d1bd9c268629cc52410f2eb76` / Draft PR #60 / run `29562903935`

关键入口：

- `root-scripts/three-repository-closure-predicates.json`：唯一 14 项 blocking catalog。
- `closure/three-repository-closure-result.json`：行为负例通过后、绑定三个 clean candidate 生成的 14/14 机器结果。
- `closure/final-ci-bindings.json`：PR、run、job、artifact digest、测试数和 excluded operations。
- `closure/final-static-audit.json`：错误资产、模切、旧桶/helper、identity、失效层和质量矩阵。
- `cross-repository-contract-digest.json`、`cross-repository-duplication/`、`non-production-joint-acceptance/`：同一组三仓候选的跨仓证据。
- `root-docs/三项目测试架构治理总计划-REOPENED-snapshot.md`：严格复审前仍为 reopened 的计划快照，避免用待审结论反向证明自己。
- `SHA256SUMS`：除清单自身外，本目录 commit A 全部文件的相对路径与 SHA-256。

commit A 推送后，以其完整 commit/tree 和 Draft PR 为输入执行反证式严格复审。只有复审全部 blocking=`PASS`，commit B 才允许加入已关单计划快照、严格复审报告和 commit A 元数据。
