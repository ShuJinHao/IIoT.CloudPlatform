# 审计整改记录

日期：2026-05-13

适用范围：`IIoT.CloudPlatform`。本记录只覆盖云端审计整改，不包含 `IIoT.EdgeClient` 或 `AICopilot`。

## 完成内容

- 为人员端请求新增 `AdminOnlyAttribute` 和 `AdminOnlyBehavior`，用于表达并执行管理员专属操作。
- `RegisterDeviceCommand` 保留 `Device.Create` 权限目录，同时增加 `AdminOnly` 标记，继续保留 Handler 内部管理员校验和失败审计。
- `RequestKindGuardBehavior` 增加 `AdminOnly` 使用边界检查，禁止把管理员专属标记挂到非 human 请求。
- 设备台账“新建设备”按钮改为仅管理员可见，避免普通用户拥有 `Device.Create` 时误解为可注册设备。
- 前端已在 2026-05-14 的 Niond 收尾中移除旧 UI 库依赖，改为项目自有 `Ui*` 控件、Tailwind v4、reka-ui、lucide 和 vue-i18n 视觉栈；Vite 分包不再包含旧 UI 库 chunk。

## 验证命令

```powershell
dotnet test src\tests\IIoT.ServiceLayer.Tests
dotnet test src\tests\IIoT.ProductionService.Tests
cd src\ui\iiot-web
npm run build
```

## 当前状态

- 后端审计整改保持原有边界。
- 前端最大主要 vendor chunk 已转为 `vendor-echarts`，旧 UI 库依赖与 chunk 已移除。
- 若后续继续压缩包体，优先评估 ECharts 按需引入，而不是恢复旧 UI 库。
