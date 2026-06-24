# IIoT Cloud Web Frontend Rules

本文件是 Cloud 前端专项规则入口。

修改 `IIoT.CloudPlatform/src/ui/iiot-web` 前必须读完本文件。

## 1. 项目边界

- 本前端属于 `IIoT.CloudPlatform`，不得顺手修改 `AICopilot` 或 `IIoT.EdgeClient`。
- 不创建跨项目共享包，不切换 pnpm workspace，不新增根目录 `packages/`，除非用户在当前轮明确授权。
- 不改 Cloud 后端 API 契约，不改设备、权限、配方、过站、发布等业务规则。
- UI 控件必须接真实业务链路；没有真实链路时隐藏、禁用或明确不可做，不伪造状态或数据。

## 2. 业务红线

- `ClientCode` 是设备 bootstrap/寻址码，不得在维护 UI 中改写或当作归档主键。
- `DeviceId` 是云端正式设备标识，产能、日志、过站记录等归档查询必须按正式身份处理。
- 新设备注册是管理员专属操作。
- 设备删除必须展示级联影响范围，二次确认，并保留权限、审计和真实 API 链路。
- 配方修改必须版本化，不得原地覆盖旧版本。
- 客户端发布页面不得伪造安装包、hash、包大小、下载地址或更新说明。
- 过站页面必须从 `GET /api/v1/human/pass-stations/types` 读取 schema 动态渲染；普通工序不得新增独立 Vue 页面。

## 3. 目录边界

- `src/core/` 放跨 feature 的基础设施：HTTP、ProblemDetails、ApiResult、auth 支撑、permissions、pagination、list-page。
- `src/shared/` 放 Cloud 内共享 UI、table cell、feedback 和布局能力。
- `src/features/{domain}/` 放业务功能切片：`api.ts`、`types.ts`、`columns.ts`、`routes.ts`、`useXxx.ts`、页面和子组件。
- `src/views/` 中的旧页面只作为待迁移入口；新页面不继续加到 `views/`。

## 4. 页面职责

- 页面 SFC 只做布局、子组件组合和命令连接。
- 新建 feature 页面 SFC 必须控制在 200 行以内。
- 弹窗、抽屉、复杂表单、复杂参数编辑器必须拆独立 SFC。
- 列表页必须使用统一 list-page composable，不在页面里重复手写分页、loading、error、empty 状态。
- 表格列定义放 `columns.ts`，不在页面 SFC 中堆复杂 `h()` 渲染函数。

## 5. HTTP 和错误反馈

- 所有 API 调用走 `src/core/http/httpClient.ts`。
- 后端 ProblemDetails 的 `detail`、`errors`、`title` 必须按优先级展示，不得用固定死文案覆盖。
- `ApiResult<T>` 业务错误和 HTTP 异常必须进入同一套反馈语义。
- 401 只能触发登录失效流程；403 必须保留无权原因展示。
- 创建、保存、删除、发布等动作必须有成功或失败反馈。

## 6. 类型和权限

- `Pagination`、`PagedMetaData`、`PagedList<T>` 统一从 `src/core/types/pagination.ts` 导入。
- DTO 优先放在对应 feature 或 API 模块；通用契约放 `src/core/types`。
- 禁止 feature 之间互相 re-export 通用类型。
- 不新增 `any`；确实未知的数据用 `unknown` 并在边界收窄。
- 页面权限判断必须使用现有权限模型，不在页面中硬编码绕过权限。

## 7. 测试和验证

- 阶段性改动至少运行 `npm run build`。
- 涉及核心 HTTP、分页、路由守卫、删除确认、schema 动态列时必须补单元测试并运行 `npm run test:unit`。
- 涉及可见 UI 布局时必须真实运行或截图验收；build 通过不等于 UI 通过。

## Pre-change Checklist

- [ ] 已读本文件和 `docs/总规则.md`、`IIoT.CloudPlatform/docs/云端规则.md`、`IIoT.CloudPlatform/docs/过站工序扩展规则.md`。
- [ ] 改动范围只在 `IIoT.CloudPlatform`。
- [ ] 没有修改后端 API 契约或业务红线。
- [ ] 新增/修改页面 SFC 不超过 200 行。
- [ ] 列表页使用统一 list-page composable。
- [ ] 表格列定义不继续堆在页面 SFC。
- [ ] API 错误展示后端返回的具体信息。
- [ ] 通用分页类型从 `src/core/types/pagination.ts` 导入。
- [ ] 前端 build 和相关单元测试通过。
