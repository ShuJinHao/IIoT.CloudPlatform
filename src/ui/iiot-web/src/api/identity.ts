import http from '../utils/http';

export interface RolePermissionsDto {
  roleName: string;
  permissions: string[];
}

export interface PermissionGroupDto {
  groupName: string;
  permissions: string[];
}

export interface DefineRolePolicyPayload {
  roleName: string;
  permissions: string[];
}

export interface ChangePasswordPayload {
  userId: string;
  currentPassword: string;
  newPassword: string;
}

export interface ResetPasswordPayload {
  userId: string;
  newPassword: string;
}

export interface UpdateUserPermissionsPayload {
  userId: string;
  permissions: string[];
}

const basePath = '/human/identity';

export const getAllRolesApi = () => http.get<string[]>(`${basePath}/roles`);

export const defineRolePolicyApi = (payload: DefineRolePolicyPayload) =>
  http.post<boolean>(`${basePath}/roles`, payload);

export const getRolePermissionsApi = (roleName: string) =>
  http.get<RolePermissionsDto>(`${basePath}/roles/${roleName}/permissions`);

export const updateRolePermissionsApi = (roleName: string, permissions: string[]) =>
  http.put<boolean>(`${basePath}/roles/${roleName}/permissions`, permissions);

export const getAllDefinedPermissionsApi = () =>
  http.get<PermissionGroupDto[]>(`${basePath}/permissions`);

export const changePasswordApi = (payload: ChangePasswordPayload) =>
  http.put<boolean>(`${basePath}/password`, payload);

export const resetPasswordApi = (payload: ResetPasswordPayload) =>
  http.put<boolean>(`${basePath}/password/reset`, payload);

export const getUserPersonalPermissionsApi = (userId: string) =>
  http.get<string[]>(`${basePath}/users/${userId}/permissions`);

export const updateUserPermissionsApi = (userId: string, payload: UpdateUserPermissionsPayload) =>
  http.put<boolean>(`${basePath}/users/${userId}/permissions`, payload);
