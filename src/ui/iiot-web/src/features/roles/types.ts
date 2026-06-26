export type RolePermissionStatus = 'loading' | 'loaded' | 'failed';

export interface RoleCreateForm {
  roleName: string;
  permissions: string[];
}

export function rolePermissionSummary(
  role: string,
  permissions: Record<string, string[]>,
  statuses: Record<string, RolePermissionStatus>,
) {
  const status = statuses[role];
  if (status === 'loaded') return `${permissions[role]?.length ?? 0} 项`;
  if (status === 'failed') return '加载失败';
  return '加载中';
}

export function togglePermission(
  permissions: string[],
  permission: string,
  checked: boolean,
) {
  if (checked) {
    if (!permissions.includes(permission)) permissions.push(permission);
    return;
  }
  const index = permissions.indexOf(permission);
  if (index > -1) permissions.splice(index, 1);
}

export function validateRoleName(roleName: string) {
  return roleName.trim() ? null : '角色名称不能为空';
}
