# Bootstrap 认证加固

## 当前方案

云端采用预共享启动密钥加固设备 bootstrap：

- 设备注册时生成 `ClientCode` 和一次性明文启动密钥。
- 数据库只保存启动密钥哈希，不保存明文。
- Edge bootstrap 继续保留 `clientCode` 查询参数。
- 启动密钥通过 `X-IIoT-Bootstrap-Secret` 请求头传递，避免进入 URL 日志。
- bootstrap 成功后仍由云端签发设备 JWT 和刷新令牌，后续上传继续使用 `DeviceId` 作为正式身份。

## 强制接入

EdgeClient 已升级为保存并发送启动密钥，生产与默认配置均使用 `BootstrapAuth:RequireSecret=true`。

`BootstrapAuth:RequireSecret=true` 时，云端会校验 `clientCode + X-IIoT-Bootstrap-Secret`，缺少或错误密钥都会拒绝签发设备 token。

## 轮换规则

管理员可以为设备轮换启动密钥。轮换后：

- 新密钥只在接口响应中返回一次。
- 旧密钥立即失效。
- 审计和日志只记录设备编码、结果和失败原因，不记录密钥明文。

## 客户端配置要求

EdgeClient 的 `CloudApi:BaseUrl` 必须指向 Gateway 地址，`CloudApi:ClientCode` 保存设备寻址码，`CloudApi:BootstrapSecret` 保存云端设备注册或轮换时返回的启动密钥。客户端不生成启动密钥，也不支持绕过 Gateway 直连 HttpApi。
