# 过站工序配置

`pass-station-types.json` 是云端过站工序字段定义文件。新增工序时默认只扩展这个文件：

- 当前生产只登记 `cp / 正极模切`、`ap / 负极模切`；注塑、叠片、匀浆不得作为活动 schema 或示例回流。
- `typeKey` 必须与云端工序编码保持一致，并使用小写字母开头的安全命名。
- `fields` 只描述工序专属字段，公共检索字段由统一接口固定提供。
- 公共检索字段包括 `deviceId`、`barcode`、`cellResult`、`completedTime`、`receivedAt`。
- CP/AP 的 schema 字段为 `plcCode`、`plcName`、`startTime`、`punchingQuantity`、`punchingSpeed`；标准传输元数据可进入统一链路，但未知业务字段仍严格拒绝。
- 高频过滤、排序、统计字段不要只藏在 JSON 中；需要单独评估是否提升为普通列或增加 JSONB 表达式索引。
- JSON 文件不能写注释，字段说明维护在本文档和 `docs/过站工序扩展规则.md`。
