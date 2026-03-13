// src/api/identity.ts
import http from '../utils/http';

// ==========================================
// DTO 类型定义
// ==========================================

export interface RolePermissionsDto {
  roleName: string;
  permissions: string[];
}

export interface PermissionGroupDto {
  groupName: string;
  permissions: string[];
}

export interface DefineRolePolicyPayload {
  RoleName: string;
  Permissions: string[];
}

export interface ChangePasswordPayload {
  UserId: string;
  CurrentPassword: string;
  NewPassword: string;
}

export interface ResetPasswordPayload {
  UserId: string;
  NewPassword: string;
}

export interface UpdateUserPermissionsPayload {
  UserId: string;
  Permissions: string[];
}

// ==========================================
// API 调用函数
// ==========================================

export const getAllRolesApi = () => {
  return http.get<string[]>('/identity/roles');
};

export const defineRolePolicyApi = (payload: DefineRolePolicyPayload) => {
  return http.post<boolean>('/identity/roles', payload);
};

export const getRolePermissionsApi = (roleName: string) => {
  return http.get<RolePermissionsDto>(`/identity/roles/${roleName}/permissions`);
};

export const updateRolePermissionsApi = (roleName: string, permissions: string[]) => {
  return http.put<boolean>(`/identity/roles/${roleName}/permissions`, permissions);
};

export const getAllDefinedPermissionsApi = () => {
  return http.get<PermissionGroupDto[]>('/identity/permissions/all');
};

export const changePasswordApi = (payload: ChangePasswordPayload) => {
  return http.put<boolean>('/identity/password', payload);
};

export const resetPasswordApi = (payload: ResetPasswordPayload) => {
  return http.put<boolean>('/identity/password/reset', payload);
};

/** 获取指定员工的个人特批权限 — GET /api/v1/identity/users/{userId}/permissions */
export const getUserPersonalPermissionsApi = (userId: string) => {
  return http.get<string[]>(`/identity/users/${userId}/permissions`);
};

/** 更新指定员工的个人特批权限 — PUT /api/v1/identity/users/{userId}/permissions */
export const updateUserPermissionsApi = (userId: string, payload: UpdateUserPermissionsPayload) => {
  return http.put<boolean>(`/identity/users/${userId}/permissions`, payload);
};
