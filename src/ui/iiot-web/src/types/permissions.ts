// src/types/permissions.ts
// 🌟 与后端 [AuthorizeRequirement("xxx")] 完全对齐的权限点常量表
// 修改后端权限点时，这里也要同步更新

export const Permissions = {
  // ===== 员工人事科 =====
  Employee: {
    Read:         'Employee.Read',
    Onboard:      'Employee.Onboard',
    Update:       'Employee.Update',
    UpdateAccess: 'Employee.UpdateAccess',
    Deactivate:   'Employee.Deactivate',
    Terminate:    'Employee.Terminate',
  },

  // ===== 工序管理 =====
  Process: {
    Read:   'Process.Read',
    Create: 'Process.Create',
    Update: 'Process.Update',
    Delete: 'Process.Delete',
  },

  // ===== 生产/设备科 =====
  Device: {
    Read:       'Device.Read',
    Create:     'Device.Create',
    Update:     'Device.Update',
    Delete:     'Device.Delete',
    CascadeDelete: 'Device.CascadeDelete',
  },

  // ===== 上位机 PLC 状态 =====
  EdgeHost: {
    Read: 'EdgeHost.Read',
  },

  // ===== 配方管理 =====
  Recipe: {
    Read:   'Recipe.Read',
    Create: 'Recipe.Create',
    Update: 'Recipe.Update',
  },

  // ===== 角色权限管理 =====
  Role: {
    Read: 'Role.Read',
    Define: 'Role.Define',
    Update: 'Role.Update',
  },

  // ===== 客户端发布 =====
  ClientRelease: {
    Read: 'ClientRelease.Read',
    GenerateInstaller: 'ClientRelease.GenerateInstaller',
    Publish: 'ClientRelease.Publish',
    Manage: 'ClientRelease.Manage',
  },
} as const;

// 提取所有权限点的联合类型，用于 TypeScript 严格校验
type PermissionGroup = typeof Permissions[keyof typeof Permissions];
export type PermissionKey = PermissionGroup[keyof PermissionGroup];
