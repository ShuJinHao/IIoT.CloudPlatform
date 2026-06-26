# IIoT.CloudPlatform Instructions

修改 `IIoT.CloudPlatform` 前先读：

- 工作区总规则：`../docs/总规则.md`
- 云端详细规则：`docs/云端规则.md`
- 过站扩展规则：`docs/过站工序扩展规则.md`
- 修改 Cloud 前端前必须先读 `src/ui/iiot-web/AGENTS.md`，遵守 Cloud 前端架构规则。

## Red Lines

- 云端是人员、权限、设备、配方、设备寻址、历史归档查询的业务源头。
- 新设备注册管理员专属。
- `ClientCode` 是设备寻址码，不能维护时改写；`DeviceId` 是正式归档标识。
- 设备删除必须检查历史依赖；配方修改必须版本化。
- 新普通过站工序默认只改配置，不重开身份链、上传主链路、部署主链路。

## Validation

- 后端改动运行匹配 build/test。
- 前端改动运行 `npm run build`。
- 涉及部署变量时同步模板和示例。
