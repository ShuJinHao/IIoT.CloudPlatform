# Cloud 生产服务权限治理记录

日期：2026-05-12

## 完成内容

- 本阶段只修改 `IIoT.CloudPlatform`，未修改 `IIoT.EdgeClient`、`AICopilot`、Launcher 或工业协议适配。
- 新增 `ICurrentUserDeviceAccessService`，统一封装当前用户 Admin 判断、用户 Id 解析、设备范围获取和单设备访问校验。
- 简化 `IDevicePermissionService`，移除 `isAdmin` 参数；EF 实现只负责查询普通用户被分配的设备集合。
- `IIoT.ProductionService` Human 端设备、配方、产能、日志、过站查询和维护用例改为通过集中访问服务获取设备范围。
- 保留业务语义：Admin 设备范围仍为 `null` 表示全量设备，普通用户仍按设备分配过滤，设备注册与启动密钥轮换仍为管理员专属操作。

## 验证结果

- `dotnet test IIoT.CloudPlatform/src/tests/IIoT.ServiceLayer.Tests --no-restore`
  - 结果：通过 152，失败 0，跳过 0。
- `dotnet build IIoT.CloudPlatform/src/hosts/IIoT.AppHost --no-restore`
  - 结果：0 warning，0 error。
- `rg -n "SystemRoles\\.Admin|isAdmin:" IIoT.CloudPlatform/src/services/IIoT.ProductionService`
  - 结果：无匹配项，生产服务旧的散落 Admin/设备范围入口已移除。
- `git -C IIoT.CloudPlatform diff --check`
  - 结果：通过，无空白错误。
- `git -C IIoT.EdgeClient status --short`、`git -C AICopilot status --short`
  - 结果：无输出，本批次未修改客户端或 AICopilot。

## 范围边界

- 未处理 `IIoT.ProductionService.Tests` 空项目。
- 未处理 `RefreshTokenSession` 是否继承 `BaseEntity`。
- 未处理 `EfRepository.Update` 全量更新策略。
- 未处理 Cloud 以外的 Edge、AICopilot、协议适配或 Launcher DI。

## 剩余风险与下一阶段

- 本阶段属于权限入口收口，后续如继续治理 Cloud，应单独评估 EF 更新策略、RefreshTokenSession 领域事件能力和 ProductionService 测试项目填充。
- 如果新增 Human 端生产服务用例，需要继续复用 `ICurrentUserDeviceAccessService`，不能重新在 Handler 内写散落的 Admin/设备范围判断。
