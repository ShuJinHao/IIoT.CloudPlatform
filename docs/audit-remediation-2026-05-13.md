# 审计整改记录

日期：2026-05-13

适用范围：`IIoT.CloudPlatform`。本记录只覆盖云端本轮审计整改，不包含 `IIoT.EdgeClient` 或 `AICopilot`。

## 完成内容

- 为人员端请求新增 `AdminOnlyAttribute` 和 `AdminOnlyBehavior`，用于表达并执行管理员专属操作。
- `RegisterDeviceCommand` 保留 `Device.Create` 权限目录，同时增加 `AdminOnly` 标记，继续保留 Handler 内部管理员校验和失败审计。
- `RequestKindGuardBehavior` 增加 `AdminOnly` 使用边界检查，禁止把管理员专属标记挂到非 human 请求。
- 设备台账“新建设备”按钮改为仅管理员可见，避免普通用户拥有 `Device.Create` 时误解为可注册设备。
- 前端 Vite 分包从单一 vendor 拆为 Vue、HTTP、Naive UI、Naive 支撑库、ECharts、ZRender 和 Vue-ECharts 等稳定 chunk。

## 验证命令

```powershell
dotnet test src\tests\IIoT.ServiceLayer.Tests
dotnet test src\tests\IIoT.ProductionService.Tests
npm run build
```

## 验证结果

- `IIoT.ServiceLayer.Tests`：通过 170 个测试。
- `IIoT.ProductionService.Tests`：通过 4 个测试。
- `npm run build`：通过。分包后最大主要 vendor 为 `vendor-naive` 约 567 KB，低于当前 600 KB 告警阈值；未再出现原先 753 KB 的 Naive UI chunk 告警。

## 剩余风险

- `vendor-naive` 仍是最大前端 chunk。若后续需要继续压到 500 KB 以下，应改造为更细的 Naive UI 按组件导入或引入自动按需导入方案，不建议在当前根导入方式下强拆包内部模块。
