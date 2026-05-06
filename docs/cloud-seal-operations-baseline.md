# Cloud 封口运维基线

## 封口口径

本基线只约束 `IIoT.CloudPlatform` 主业务链路。当前封口目标是：后续新增生产业务模块时，不重开 bootstrap、设备身份、上传幂等、Outbox、DataWorker、权限主链路、部署健康检查和通用错误响应。

本轮封口不包含 AiRead。`/api/v1/ai/read/*` 属于独立 AI-facing read-only API surface，必须单独完成审核、service account 签发/轮换/撤销说明和合并验收后，才能进入主线基线。

## 必须满足的业务闸门

- 新设备注册必须是 AdminOnly。`Device.Create` 可以保留为人端权限目录，但不能替代管理员硬约束。
- 设备删除前必须继续检查配方、产能、日志、过站或生产记录依赖。
- 配方修改必须继续使用版本化流转，不允许原地覆盖活动版本。
- Edge 上传必须继续使用正式 `DeviceId`、设备 token 绑定、上传限制、幂等登记和 Outbox。
- 新增过站类工序默认只扩展工序配置、payload、展示字段和必要测试，不重开通用上传主链路。

## 数据库运维基线

当前记录表已经依赖 TimescaleDB hypertable 承载高频数据，但 compression、retention、continuous aggregate、reporting DB/read replica 属于后续容量专项，不作为本轮主业务封口阻断项。

本轮 `ops-check.sh` 必须输出以下只读观测项：

- TimescaleDB extension 版本。
- `device_logs`、`hourly_capacity`、`pass_station_records` 的 hypertable 状态。
- 三张记录表的表总大小、索引大小和 chunk 数。
- `upload_receive_registrations` 行数。
- Outbox backlog 和最老 pending outbox age。
- Timescale compression/retention policy 是否存在。

其中 hypertable 缺失属于运行风险；compression/retention policy 缺失只输出状态，不在本轮阻断封口。

## 验收命令

```bash
sh -n deploy/scripts/ops-check.sh
docker compose --env-file deploy/.env.example -f deploy/docker-compose.prod.yml config -q
```

如果本地或服务器已有完整生产 compose 在运行，再执行：

```bash
./deploy/scripts/ops-check.sh
```

若没有运行中的生产 compose，本轮 PR 只要求完成脚本语法校验和 compose 配置校验，并在 PR 说明中标注未运行实机 ops-check。
