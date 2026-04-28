import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { jwtDecode } from 'jwt-decode';
import { refreshHumanSessionApi, type AuthSessionPayload } from '../api/auth';
import type { PermissionKey } from '../types/permissions';

const ROLE_CLAIM_KEY = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const PERMISSION_CLAIM_KEY = 'Permission';
const REFRESH_LEAD_TIME_MS = 2 * 60 * 1000;
const REFRESH_LOCK_TTL_MS = 30 * 1000;
const REFRESH_LOCK_POLL_INTERVAL_MS = 250;
const AUTH_SYNC_EVENT_KEY = 'iiot-auth-sync-event';
const AUTH_REFRESH_LOCK_KEY = 'iiot-auth-refresh-lock';
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

interface SessionMutationOptions {
  broadcast?: boolean;
}

interface LogoutOptions extends SessionMutationOptions {
  redirectToLogin?: boolean;
}

interface AuthSyncEvent {
  type: 'session-updated' | 'logout';
  tabId: string;
  at: number;
  session?: AuthSessionPayload;
}

interface RefreshLockPayload {
  ownerTabId: string;
  expiresAt: number;
}

const tabId =
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `tab-${Date.now()}-${Math.random().toString(16).slice(2)}`;

let refreshTimer: number | null = null;
let refreshPromise: Promise<string | null> | null = null;
let storageSyncInitialized = false;

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

  function setSession(
    session: AuthSessionPayload,
    options: SessionMutationOptions = {},
  ) {
    applySession(session);

    if (options.broadcast !== false) {
      emitAuthSyncEvent({
        type: 'session-updated',
        tabId,
        at: Date.now(),
        session,
      });
    }
  }

  async function restoreFromStorage() {
    initializeStorageSync();

    const savedSession = readStoredSession();
    if (!savedSession) {
      logout({ broadcast: false });
      return;
    }

    try {
      if (new Date(savedSession.refreshTokenExpiresAt).getTime() <= Date.now()) {
        logout({ broadcast: false });
        return;
      }

      if (new Date(savedSession.accessTokenExpiresAt).getTime() <= Date.now() + REFRESH_LEAD_TIME_MS) {
        setSession(savedSession, { broadcast: false });
        await refreshSession();
        return;
      }

      setSession(savedSession, { broadcast: false });
    } catch {
      logout({ broadcast: false });
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

    refreshPromise = coordinateRefresh();

    try {
      return await refreshPromise;
    } finally {
      refreshPromise = null;
    }
  }

  function logout(options: LogoutOptions = {}) {
    clearSessionState();
    clearStoredSession();
    releaseRefreshLock();

    if (options.broadcast !== false) {
      emitAuthSyncEvent({
        type: 'logout',
        tabId,
        at: Date.now(),
      });
    }

    if (options.redirectToLogin && window.location.pathname !== '/login') {
      window.location.href = '/login';
    }
  }

  function hasPermission(permission: PermissionKey | string): boolean {
    if (isAdmin.value) return true;
    return permissions.value.includes(permission);
  }

  function hasAllPermissions(perms: (PermissionKey | string)[]): boolean {
    if (isAdmin.value) return true;
    return perms.every((p) => permissions.value.includes(p));
  }

  async function coordinateRefresh(): Promise<string | null> {
    if (tryAcquireRefreshLock()) {
      return executeOwnedRefresh();
    }

    return waitForPeerRefresh();
  }

  async function executeOwnedRefresh(): Promise<string | null> {
    try {
      const session = await refreshHumanSessionApi(refreshToken.value!);
      setSession(session);
      return session.accessToken;
    } catch {
      logout();
      return null;
    } finally {
      releaseRefreshLock();
    }
  }

  async function waitForPeerRefresh(): Promise<string | null> {
    const originalRefreshToken = refreshToken.value;
    const deadline = Date.now() + REFRESH_LOCK_TTL_MS + 1500;

    while (Date.now() < deadline) {
      const storedSession = readStoredSession();
      if (!storedSession) {
        logout({ broadcast: false });
        return null;
      }

      if (
        storedSession.refreshToken !== originalRefreshToken ||
        storedSession.accessToken !== token.value ||
        storedSession.accessTokenExpiresAt !== accessTokenExpiresAt.value
      ) {
        setSession(storedSession, { broadcast: false });
        return storedSession.accessToken;
      }

      const lock = readRefreshLock();
      if (!lock || lock.expiresAt <= Date.now()) {
        break;
      }

      await delay(REFRESH_LOCK_POLL_INTERVAL_MS);
    }

    if (tryAcquireRefreshLock()) {
      return executeOwnedRefresh();
    }

    const latestSession = readStoredSession();
    if (!latestSession) {
      logout({ broadcast: false });
      return null;
    }

    setSession(latestSession, { broadcast: false });
    return latestSession.accessToken;
  }

  function applySession(session: AuthSessionPayload) {
    token.value = session.accessToken;
    refreshToken.value = session.refreshToken;
    accessTokenExpiresAt.value = session.accessTokenExpiresAt;
    refreshTokenExpiresAt.value = session.refreshTokenExpiresAt;

    localStorage.setItem(STORAGE_KEYS.token, session.accessToken);
    localStorage.setItem(STORAGE_KEYS.refreshToken, session.refreshToken);
    localStorage.setItem(STORAGE_KEYS.accessTokenExpiresAt, session.accessTokenExpiresAt);
    localStorage.setItem(STORAGE_KEYS.refreshTokenExpiresAt, session.refreshTokenExpiresAt);

    parseClaims(session.accessToken);
    scheduleRefresh(session.accessTokenExpiresAt);
  }

  function clearSessionState() {
    token.value = null;
    refreshToken.value = null;
    accessTokenExpiresAt.value = null;
    refreshTokenExpiresAt.value = null;
    userId.value = '';
    employeeNo.value = '';
    role.value = '';
    permissions.value = [];
    clearRefreshTimer();
  }

  function clearStoredSession() {
    localStorage.removeItem(STORAGE_KEYS.token);
    localStorage.removeItem(STORAGE_KEYS.refreshToken);
    localStorage.removeItem(STORAGE_KEYS.accessTokenExpiresAt);
    localStorage.removeItem(STORAGE_KEYS.refreshTokenExpiresAt);
  }

  function readStoredSession(): AuthSessionPayload | null {
    const savedToken = localStorage.getItem(STORAGE_KEYS.token);
    const savedRefreshToken = localStorage.getItem(STORAGE_KEYS.refreshToken);
    const savedAccessTokenExpiresAt = localStorage.getItem(STORAGE_KEYS.accessTokenExpiresAt);
    const savedRefreshTokenExpiresAt = localStorage.getItem(STORAGE_KEYS.refreshTokenExpiresAt);

    if (!savedToken || !savedRefreshToken || !savedAccessTokenExpiresAt || !savedRefreshTokenExpiresAt) {
      return null;
    }

    return {
      accessToken: savedToken,
      refreshToken: savedRefreshToken,
      accessTokenExpiresAt: savedAccessTokenExpiresAt,
      refreshTokenExpiresAt: savedRefreshTokenExpiresAt,
    };
  }

  function emitAuthSyncEvent(event: AuthSyncEvent) {
    localStorage.setItem(AUTH_SYNC_EVENT_KEY, JSON.stringify(event));
  }

  function initializeStorageSync() {
    if (storageSyncInitialized || typeof window === 'undefined') {
      return;
    }

    window.addEventListener('storage', handleStorageSync);
    storageSyncInitialized = true;
  }

  function handleStorageSync(event: StorageEvent) {
    if (event.key !== AUTH_SYNC_EVENT_KEY || !event.newValue) {
      return;
    }

    const payload = parseSyncEvent(event.newValue);
    if (!payload || payload.tabId === tabId) {
      return;
    }

    if (payload.type === 'session-updated' && payload.session) {
      setSession(payload.session, { broadcast: false });
      return;
    }

    if (payload.type === 'logout') {
      logout({ broadcast: false });
    }
  }

  function parseClaims(rawToken: string) {
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

  function scheduleRefresh(expiresAt: string) {
    clearRefreshTimer();

    const delayMs = new Date(expiresAt).getTime() - Date.now() - REFRESH_LEAD_TIME_MS;
    if (delayMs <= 0) {
      void refreshSession();
      return;
    }

    refreshTimer = window.setTimeout(() => {
      void refreshSession();
    }, delayMs);
  }

  function clearRefreshTimer() {
    if (refreshTimer !== null) {
      window.clearTimeout(refreshTimer);
      refreshTimer = null;
    }
  }

  function tryAcquireRefreshLock(): boolean {
    const currentLock = readRefreshLock();
    if (currentLock && currentLock.ownerTabId !== tabId && currentLock.expiresAt > Date.now()) {
      return false;
    }

    const nextLock: RefreshLockPayload = {
      ownerTabId: tabId,
      expiresAt: Date.now() + REFRESH_LOCK_TTL_MS,
    };

    localStorage.setItem(AUTH_REFRESH_LOCK_KEY, JSON.stringify(nextLock));

    const confirmed = readRefreshLock();
    return confirmed?.ownerTabId === tabId;
  }

  function releaseRefreshLock() {
    const lock = readRefreshLock();
    if (lock?.ownerTabId === tabId) {
      localStorage.removeItem(AUTH_REFRESH_LOCK_KEY);
    }
  }

  function readRefreshLock(): RefreshLockPayload | null {
    const rawLock = localStorage.getItem(AUTH_REFRESH_LOCK_KEY);
    if (!rawLock) {
      return null;
    }

    try {
      const parsed = JSON.parse(rawLock) as RefreshLockPayload;
      if (
        typeof parsed.ownerTabId !== 'string' ||
        typeof parsed.expiresAt !== 'number'
      ) {
        return null;
      }

      return parsed;
    } catch {
      return null;
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

function parseSyncEvent(rawEvent: string): AuthSyncEvent | null {
  try {
    const payload = JSON.parse(rawEvent) as AuthSyncEvent;
    if (
      (payload.type !== 'session-updated' && payload.type !== 'logout') ||
      typeof payload.tabId !== 'string' ||
      typeof payload.at !== 'number'
    ) {
      return null;
    }

    return payload;
  } catch {
    return null;
  }
}

function delay(timeoutMs: number): Promise<void> {
  return new Promise((resolve) => {
    window.setTimeout(resolve, timeoutMs);
  });
}
