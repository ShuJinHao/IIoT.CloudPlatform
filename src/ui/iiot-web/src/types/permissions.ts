// src/types/permissions.ts
// 🌟 与后端 [AuthorizeRequirement("xxx")] 完全对齐的权限点常量表
// 修改后端权限点时，这里也要同步更新

export const Permissions = {
  // ===== 员工人事科 =====
  Employee: {
    Read:       'Employee.Read',
    Onboard:    'Employee.Onboard',
    Update:     'Employee.Update',
    Deactivate: 'Employee.Deactivate',
    Terminate:  'Employee.Terminate',
  },

  // ===== 生产/设备科 =====
  Device: {
    Read:       'Device.Read',
    Create:     'Device.Create',
    Deactivate: 'Device.Deactivate',
  },

  // ===== 配方管理 =====
  Recipe: {
    Read:   'Recipe.Read',
    Create: 'Recipe.Create',
  },

  // ===== 角色权限管理 =====
  Role: {
    Define: 'Role.Define',
    Update: 'Role.Update',
  },
} as const;

// 提取所有权限点的联合类型，用于 TypeScript 严格校验
type PermissionGroup = typeof Permissions[keyof typeof Permissions];
export type PermissionKey = PermissionGroup[keyof PermissionGroup];
