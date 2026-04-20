import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { jwtDecode } from 'jwt-decode';
import { refreshHumanSessionApi, type AuthSessionPayload } from '../api/auth';
import type { PermissionKey } from '../types/permissions';

const ROLE_CLAIM_KEY = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const PERMISSION_CLAIM_KEY = 'Permission';
const REFRESH_LEAD_TIME_MS = 2 * 60 * 1000;
const STORAGE_KEYS = {
  token: 'token',
  refreshToken: 'refreshToken',
  accessTokenExpiresAt: 'accessTokenExpiresAt',
  refreshTokenExpiresAt: 'refreshTokenExpiresAt',
} as const;

interface JwtPayload {
  sub: string;
  unique_name: string;
  exp: number;
  [key: string]: unknown;
}

let refreshTimer: number | null = null;
let refreshPromise: Promise<string | null> | null = null;

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(STORAGE_KEYS.token));
  const refreshToken = ref<string | null>(localStorage.getItem(STORAGE_KEYS.refreshToken));
  const accessTokenExpiresAt = ref<string | null>(localStorage.getItem(STORAGE_KEYS.accessTokenExpiresAt));
  const refreshTokenExpiresAt = ref<string | null>(localStorage.getItem(STORAGE_KEYS.refreshTokenExpiresAt));
  const userId = ref<string>('');
  const employeeNo = ref<string>('');
  const role = ref<string>('');
  const permissions = ref<string[]>([]);

  const isAuthenticated = computed(() => !!token.value);
  const isAdmin = computed(() => role.value === 'Admin');

  function setSession(session: AuthSessionPayload) {
    token.value = session.accessToken;
    refreshToken.value = session.refreshToken;
    accessTokenExpiresAt.value = session.accessTokenExpiresAt;
    refreshTokenExpiresAt.value = session.refreshTokenExpiresAt;

    localStorage.setItem(STORAGE_KEYS.token, session.accessToken);
    localStorage.setItem(STORAGE_KEYS.refreshToken, session.refreshToken);
    localStorage.setItem(STORAGE_KEYS.accessTokenExpiresAt, session.accessTokenExpiresAt);
    localStorage.setItem(STORAGE_KEYS.refreshTokenExpiresAt, session.refreshTokenExpiresAt);

    _parseClaims(session.accessToken);
    _scheduleRefresh(session.accessTokenExpiresAt);
  }

  async function restoreFromStorage() {
    const savedToken = localStorage.getItem(STORAGE_KEYS.token);
    const savedRefreshToken = localStorage.getItem(STORAGE_KEYS.refreshToken);
    const savedAccessTokenExpiresAt = localStorage.getItem(STORAGE_KEYS.accessTokenExpiresAt);
    const savedRefreshTokenExpiresAt = localStorage.getItem(STORAGE_KEYS.refreshTokenExpiresAt);

    if (!savedToken || !savedRefreshToken || !savedAccessTokenExpiresAt || !savedRefreshTokenExpiresAt) {
      logout();
      return;
    }

    try {
      if (new Date(savedRefreshTokenExpiresAt).getTime() <= Date.now()) {
        logout();
        return;
      }

      if (new Date(savedAccessTokenExpiresAt).getTime() <= Date.now() + REFRESH_LEAD_TIME_MS) {
        await refreshSession();
        return;
      }

      setSession({
        accessToken: savedToken,
        refreshToken: savedRefreshToken,
        accessTokenExpiresAt: savedAccessTokenExpiresAt,
        refreshTokenExpiresAt: savedRefreshTokenExpiresAt,
      });
    } catch {
      logout();
    }
  }

  async function refreshSession(): Promise<string | null> {
    if (!refreshToken.value) {
      logout();
      return null;
    }

    if (refreshPromise) {
      return refreshPromise;
    }

    refreshPromise = (async () => {
      try {
        const session = await refreshHumanSessionApi(refreshToken.value!);
        setSession(session);
        return session.accessToken;
      } catch {
        logout();
        return null;
      } finally {
        refreshPromise = null;
      }
    })();

    return refreshPromise;
  }

  function logout() {
    token.value = null;
    refreshToken.value = null;
    accessTokenExpiresAt.value = null;
    refreshTokenExpiresAt.value = null;
    userId.value = '';
    employeeNo.value = '';
    role.value = '';
    permissions.value = [];
    _clearRefreshTimer();

    localStorage.removeItem(STORAGE_KEYS.token);
    localStorage.removeItem(STORAGE_KEYS.refreshToken);
    localStorage.removeItem(STORAGE_KEYS.accessTokenExpiresAt);
    localStorage.removeItem(STORAGE_KEYS.refreshTokenExpiresAt);
  }

  function hasPermission(permission: PermissionKey | string): boolean {
    if (isAdmin.value) return true;
    return permissions.value.includes(permission);
  }

  function hasAllPermissions(perms: (PermissionKey | string)[]): boolean {
    if (isAdmin.value) return true;
    return perms.every((p) => permissions.value.includes(p));
  }

  function _parseClaims(rawToken: string) {
    const payload = jwtDecode<JwtPayload>(rawToken);

    if (payload.exp * 1000 < Date.now()) {
      throw new Error('Token expired.');
    }

    userId.value = payload.sub;
    employeeNo.value = payload.unique_name;

    const rawRole = payload[ROLE_CLAIM_KEY];
    role.value = Array.isArray(rawRole)
      ? (rawRole[0] as string)
      : ((rawRole as string) ?? '');

    const rawPerms = payload[PERMISSION_CLAIM_KEY];
    if (!rawPerms) {
      permissions.value = [];
    } else if (Array.isArray(rawPerms)) {
      permissions.value = rawPerms as string[];
    } else {
      permissions.value = [rawPerms as string];
    }
  }

  function _scheduleRefresh(expiresAt: string) {
    _clearRefreshTimer();

    const delay = new Date(expiresAt).getTime() - Date.now() - REFRESH_LEAD_TIME_MS;
    if (delay <= 0) {
      void refreshSession();
      return;
    }

    refreshTimer = window.setTimeout(() => {
      void refreshSession();
    }, delay);
  }

  function _clearRefreshTimer() {
    if (refreshTimer !== null) {
      window.clearTimeout(refreshTimer);
      refreshTimer = null;
    }
  }

  return {
    token,
    refreshToken,
    accessTokenExpiresAt,
    refreshTokenExpiresAt,
    userId,
    employeeNo,
    role,
    permissions,
    isAuthenticated,
    isAdmin,
    setSession,
    restoreFromStorage,
    refreshSession,
    logout,
    hasPermission,
    hasAllPermissions,
  };
});
