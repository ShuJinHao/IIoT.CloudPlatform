export interface EmployeeOnboardForm {
  EmployeeNo: string;
  RealName: string;
  Password: string;
  RoleName: string | null;
}

export interface EmployeeEditForm {
  RealName: string;
}

export interface EmployeeAccessForm {
  DeviceIds: string[];
}

export interface EmployeeRoleForm {
  Selection: string;
}

export const EMPLOYEE_ROLE_CLEAR_SELECTION = 'employee-role:clear';

export function employeeRoleSelectionValue(roleName: string): string {
  return `employee-role:value:${encodeURIComponent(roleName)}`;
}

export function isAdminLikeRoleName(roleName: string): boolean {
  return roleName.trim().toLowerCase() === 'admin';
}

export function normalizeAssignableRoleNames(roleNames: readonly string[]): string[] {
  const normalizedRoles: string[] = [];
  const seen = new Set<string>();

  for (const roleName of roleNames) {
    const normalizedRoleName = roleName.trim();
    const comparisonKey = normalizedRoleName.toLowerCase();
    if (!normalizedRoleName || isAdminLikeRoleName(normalizedRoleName) || seen.has(comparisonKey)) {
      continue;
    }

    seen.add(comparisonKey);
    normalizedRoles.push(normalizedRoleName);
  }

  return normalizedRoles;
}

export interface EmployeeResetPasswordForm {
  newPwd: string;
  confirm: string;
}

export interface EmployeeConfirmDialogState {
  show: boolean;
  title: string;
  desc: string;
  confirmText: string;
  confirmType: 'success' | 'warning' | 'error';
  onConfirm: () => Promise<void>;
}

export function isResetPasswordInvalid(newPwd: string, confirm: string): string | null {
  if (!newPwd || !confirm) return '请输入新密码';
  if (newPwd !== confirm) return '两次输入的密码不一致';
  return null;
}
