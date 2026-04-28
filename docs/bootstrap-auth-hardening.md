# Bootstrap 认证加固设计占位

## 当前批次边界

本批不修改 `EdgeBootstrapController` 的匿名访问方式，也不删除 `clientCode` 查询参数。原因是 EdgeClient 当前仍依赖 `ClientCode -> bootstrap -> DeviceId` 的启动链路，强制切换会直接打断设备启动。

## 下一阶段默认方案

下一阶段默认采用预共享启动密钥方案：

- 设备注册后，云端生成 `ClientCode` 和一次性启动密钥。
- EdgeClient 首次 bootstrap 时同时提交 `clientCode` 和启动密钥。
- 云端验证通过后签发设备上传令牌和刷新令牌。
- 启动密钥只用于 bootstrap，不作为后续上传身份。
- 后续仍以 `DeviceId` 和云端签发的设备 JWT 作为正式上传身份。

## 备选方案

- mTLS：安全边界更强，但需要证书签发、安装、轮换和吊销流程。
- 一次性激活密钥：适合首次激活，但设备重装和现场恢复流程需要额外设计。

## 兼容约束

在 EdgeClient 完成配合前，云端必须保留现有 `clientCode` 查询参数和 bootstrap 路由。任何强认证上线都需要同步修改 EdgeClient 启动配置、现场交付流程和回滚方案。
