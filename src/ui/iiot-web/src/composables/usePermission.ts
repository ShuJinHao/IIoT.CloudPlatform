import { computed } from 'vue';
import { useAuthStore } from '../stores/auth';
import type { PermissionKey } from '../types/permissions';

/**
 * 权限检查 composable
 * 与 v-permission 指令互补：
 *   - v-permission：模板中按钮级隐藏
 *   - usePermission：脚本里做条件分支、计算属性、菜单过滤
 *
 * 自动短路：管理员（isAdmin）所有 has* 检查都返回 true，与 store 一致。
 */

export type PermissionValue = PermissionKey | string;

export function usePermission() {
  const auth = useAuthStore();

  function has(code: PermissionValue): boolean {
    return auth.hasPermission(code);
  }

  function hasAll(codes: PermissionValue[]): boolean {
    return auth.hasAllPermissions(codes);
  }

  function hasAny(codes: PermissionValue[]): boolean {
    if (auth.isAdmin) return true;
    return codes.some((c) => auth.hasPermission(c));
  }

  const isAdmin = computed(() => auth.isAdmin);
  const isAuthenticated = computed(() => auth.isAuthenticated);

  return {
    has,
    can: has,
    hasAll,
    hasAny,
    isAdmin,
    isAuthenticated,
  };
}
