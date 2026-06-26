export interface EmployeeOnboardForm {
  EmployeeNo: string;
  RealName: string;
  Password: string;
  RoleName: string | null;
}

export interface EmployeeEditForm {
  RealName: string;
  IsActive: boolean;
  RoleName: string | null;
}

export interface EmployeeAccessForm {
  DeviceIds: string[];
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
  onConfirm: () => Promise<void>;
}

export function isResetPasswordInvalid(newPwd: string, confirm: string): string | null {
  if (!newPwd || !confirm) return '请输入新密码';
  if (newPwd !== confirm) return '两次输入的密码不一致';
  return null;
}
