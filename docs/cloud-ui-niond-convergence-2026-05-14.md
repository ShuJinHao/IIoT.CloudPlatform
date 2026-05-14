# 云端前端 Niond 风格收敛记录

## 完成内容

- 仅收敛 `src/ui/iiot-web` 云端前端展示层，没有修改后端、API、Pinia store、router 权限逻辑、Edge 或 AICopilot。
- 将 Login、Dashboard、AppShell 已确认的 Niond 风格继续沉淀到全站列表页。
- 员工、设备、工序、配方、产能看板、产能详情、设备日志、过站追踪、角色权限页面接入统一的 `NiondDataPage`、`NiondToolbar`、`NiondTableCard` 页面结构。
- 统一表格卡片、筛选工具栏、分页、表单标签、弹窗标题、详情分组、状态提示的圆角、边框、阴影、间距和 focus 规则。
- 将页面内正字距收敛为 `letter-spacing: 0`，避免中英文混排和中文标签出现不稳定视觉。
- `NiondDataPage` 支持 `pageKey`，静态页面标题和副标题继续走 `vue-i18n`，默认中文，英文仅通过语言切换显示。

## 验证命令

- `cd src/ui/iiot-web && npm run build`
- `rg "Niond|Upgrade|\\$4|Plant Manager|Regular Sell|Search production|#c8ff3d|#d8ff72|bg-glow|bg-grid|focus-within:ring-4|letter-spacing:\\s*-" src/ui/iiot-web/src`

## 验证结果

- `npm run build` 已通过。
- 静态扫描未发现参考图业务残留、旧光晕/网格、刺眼绿色 focus 或负字距。
- 配方页中的 `Upgrade` 只作为“配方版本升级”业务变量名存在，不属于参考图 Pro/美刀残留。

## 剩余风险

- 当前浏览器自动截图环境中，受保护路由的 headless CDP 验收不稳定，最终视觉仍建议在已登录浏览器里人工检查一次。
- 本轮未改变接口、权限、分页、筛选、新增、编辑、删除等业务逻辑；如后续要继续提高视觉一致性，可再逐页减少历史页面内的局部 CSS。
