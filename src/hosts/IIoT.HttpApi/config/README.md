# 过站工序配置

`pass-station-types.json` 是 Cloud 已确认业务记录类别的兼容 Schema 注册表。设备插件版本自身的权威能力来自其签名 `data-capabilities.json`；本文件不能替代插件发布能力，也不能把业务记录类别重新定义成工序：

- 当前 AP、CP 插件只启用共同确认的 `die-cutting-completion / 模切完成记录`；历史 `ap`、`cp` 仅登记在 `legacyTypeKeys` 中用于兼容已有数据，不代表正负极工序、插件或设备身份。
- `typeKey` 是插件声明的业务记录类别，只需在该插件版本内唯一；一个插件可以声明多种。它必须使用路径安全的规范化命名，但不得要求等于 Cloud 工序编码、`ModuleId` 或 `ClientCode`。
- 新业务类别必须先取得业务确认，并同步插件 `data-capabilities.json`、发布签名、Cloud 兼容注册、Human/AiRead Schema 查询和受影响测试；不能只改本文件就宣称插件具备该能力。
- `fields` 只描述工序专属字段，公共检索字段由统一接口固定提供。
- 公共检索字段包括 `deviceId`、`barcode`、`cellResult`、`completedTime`、`receivedAt`。
- 当前模切完成 Schema 字段为 `plcCode`、`plcName`、`clipSlot`、`startTime`、`punchingQuantity`、`punchingSpeed`；标准传输元数据可进入统一链路，但当前实际插件版本未声明的业务字段仍严格拒绝。
- 高频过滤、排序、统计字段不要只藏在 JSON 中；需要单独评估是否提升为普通列或增加 JSONB 表达式索引。
- JSON 文件不能写注释，字段说明维护在本文档和 `docs/过站工序扩展规则.md`。
