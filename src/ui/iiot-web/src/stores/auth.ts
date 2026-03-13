// src/stores/auth.ts
import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { jwtDecode } from 'jwt-decode';
import type { PermissionKey } from '../types/permissions';

// 后端 ClaimTypes.Role 序列化后的完整 URI key
const ROLE_CLAIM_KEY = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
// 后端直接用字符串添加的 Permission claim key
const PERMISSION_CLAIM_KEY = 'Permission';

// JWT Payload 用宽松类型，兼容长 URI claim key
interface JwtPayload {
  sub: string;
  unique_name: string;
  exp: number;
  [key: string]: unknown;
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem('token'));
  const userId = ref<string>('');
  const employeeNo = ref<string>('');
  const role = ref<string>('');
  const permissions = ref<string[]>([]);

  const isAuthenticated = computed(() => !!token.value);
  const isAdmin = computed(() => role.value === 'Admin');

  function setToken(rawToken: string) {
    token.value = rawToken;
    localStorage.setItem('token', rawToken);
    _parseClaims(rawToken);
  }

  function restoreFromStorage() {
    const savedToken = localStorage.getItem('token');
    if (savedToken) {
      try {
        _parseClaims(savedToken);
        token.value = savedToken;
      } catch {
        logout();
      }
    }
  }

  function logout() {
    token.value = null;
    userId.value = '';
    employeeNo.value = '';
    role.value = '';
    permissions.value = [];
    localStorage.removeItem('token');
  }

  function hasPermission(permission: PermissionKey | string): boolean {
    if (isAdmin.value) return true;
    return permissions.value.includes(permission);
  }

  function hasAllPermissions(perms: (PermissionKey | string)[]): boolean {
    if (isAdmin.value) return true;
    return perms.every(p => permissions.value.includes(p));
  }

  function _parseClaims(rawToken: string) {
    const payload = jwtDecode<JwtPayload>(rawToken);

    if (payload.exp * 1000 < Date.now()) {
      throw new Error('Token 已过期');
    }

    userId.value = payload.sub;
    employeeNo.value = payload.unique_name;

    // 🌟 核心修复：后端用 ClaimTypes.Role 添加角色
    // 序列化后 key 是完整 URI，需要用完整 key 读取
    const rawRole = payload[ROLE_CLAIM_KEY];
    role.value = Array.isArray(rawRole)
      ? (rawRole[0] as string)
      : ((rawRole as string) ?? '');

    // Permission 是直接用字符串 "Permission" 添加的 claim，key 就是 "Permission"
    const rawPerms = payload[PERMISSION_CLAIM_KEY];
    if (!rawPerms) {
      permissions.value = [];
    } else if (Array.isArray(rawPerms)) {
      permissions.value = rawPerms as string[];
    } else {
      permissions.value = [rawPerms as string];
    }
  }

  return {
    token, userId, employeeNo, role, permissions,
    isAuthenticated, isAdmin,
    setToken, restoreFromStorage, logout,
    hasPermission, hasAllPermissions,
  };
});
