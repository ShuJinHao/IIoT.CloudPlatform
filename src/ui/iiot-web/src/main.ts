// @ts-ignore
import '@fontsource-variable/inter/index.css';
import './styles/tokens.css';
import './styles/global.css';
import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import router from './router';
import permissionDirective from './directives/permission';
import { useAuthStore } from './stores/auth';
import { i18n } from './i18n';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(router);
app.use(i18n);
app.directive('permission', permissionDirective);

const authStore = useAuthStore();
const initialLocation = `${window.location.pathname}${window.location.search}${window.location.hash}`;
const CHUNK_LOAD_RETRY_KEY = 'iiot-web-chunk-load-retry';

router.onError((error) => {
  recoverFromChunkLoadError(error);
});

window.addEventListener('error', (event) => {
  if (recoverFromChunkLoadError(event.error ?? event.message)) {
    event.preventDefault();
  }
});

window.addEventListener('unhandledrejection', (event) => {
  if (recoverFromChunkLoadError(event.reason)) {
    event.preventDefault();
  }
});

app.mount('#app');
void waitForRouterReady()
  .then(() => {
    sessionStorage.removeItem(CHUNK_LOAD_RETRY_KEY);
  });
void restoreAuthentication(initialLocation);

async function restoreAuthentication(returnUrl: string) {
  try {
    await authStore.restoreFromStorage();
  } catch (error) {
    console.error('Failed to restore authentication session.', error);
    authStore.logout({ broadcast: false });
  }

  const routeReady = await waitForRouterReady();
  if (!routeReady) {
    return;
  }

  const currentRoute = router.currentRoute.value;
  if (!authStore.isAuthenticated && currentRoute.meta.requiresAuth !== false && currentRoute.name !== 'Login') {
    await replaceRoute({ name: 'Login', query: { returnUrl } });
    return;
  }

  if (authStore.isAuthenticated && currentRoute.name === 'Login') {
    const loginReturnUrl = resolveLoginReturnUrl(currentRoute.query.returnUrl, returnUrl);
    if (loginReturnUrl) {
      await replaceRoute(loginReturnUrl);
    }
    return;
  }

  if (
    authStore.isAuthenticated &&
    currentRoute.name === 'Forbidden' &&
    isSafeLocalPath(returnUrl) &&
    returnUrl !== '/forbidden'
  ) {
    await replaceRoute(returnUrl);
  }
}

async function replaceRoute(location: Parameters<typeof router.replace>[0]) {
  try {
    await router.replace(location);
  } catch (error) {
    if (!recoverFromChunkLoadError(error)) {
      console.error('Failed to replace route after authentication restore.', error);
    }
  }
}

async function waitForRouterReady(): Promise<boolean> {
  try {
    await router.isReady();
    return true;
  } catch (error) {
    if (!recoverFromChunkLoadError(error)) {
      console.error('Failed to resolve initial route.', error);
    }
    return false;
  }
}

function recoverFromChunkLoadError(error: unknown): boolean {
  if (!isChunkLoadError(error)) {
    return false;
  }

  console.error('Detected stale frontend chunk. Reloading current application version.', error);
  if (sessionStorage.getItem(CHUNK_LOAD_RETRY_KEY) === '1') {
    renderChunkLoadFailure();
    return true;
  }

  sessionStorage.setItem(CHUNK_LOAD_RETRY_KEY, '1');
  window.location.reload();
  return true;
}

function isChunkLoadError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error ?? '');
  return /Failed to fetch dynamically imported module|Failed to load module script|Importing a module script failed|Loading chunk|ChunkLoadError|error loading dynamically imported module|Expected a JavaScript-or-Wasm module script/i.test(message);
}

function renderChunkLoadFailure() {
  const appRoot = document.querySelector('#app');
  if (!appRoot) {
    return;
  }

  appRoot.innerHTML = `
    <main style="min-height:100vh;display:grid;place-items:center;padding:24px;background:#f6f7f9;color:#111827;font-family:Inter,system-ui,sans-serif;">
      <section style="max-width:420px;border:1px solid #e5e7eb;background:#fff;border-radius:8px;padding:24px;box-shadow:0 12px 28px rgba(15,23,42,.08);">
        <h1 style="margin:0 0 12px;font-size:20px;line-height:1.4;">应用版本已更新</h1>
        <p style="margin:0 0 18px;font-size:14px;line-height:1.7;color:#4b5563;">请刷新页面后重新进入系统。</p>
        <button type="button" data-reload-button style="height:38px;border:0;border-radius:6px;background:#2563eb;color:#fff;padding:0 16px;font-weight:700;cursor:pointer;">刷新</button>
      </section>
    </main>`;
  appRoot.querySelector('[data-reload-button]')?.addEventListener('click', () => {
    window.location.reload();
  });
}

function isSafeLocalPath(value: string): boolean {
  return value.startsWith('/') && !value.startsWith('//') && !value.includes('\\');
}

function isSafeLocalReturnUrl(value: unknown): value is string {
  return typeof value === 'string' && isSafeLocalPath(value);
}

function resolveLoginReturnUrl(routeReturnUrl: unknown, initialReturnUrl: string): string | null {
  const candidate = isSafeLocalReturnUrl(routeReturnUrl) ? routeReturnUrl : initialReturnUrl;
  if (
    !isSafeLocalPath(candidate) ||
    candidate === '/login' ||
    candidate.startsWith('/login?') ||
    candidate.startsWith('/connect/')
  ) {
    return null;
  }

  return candidate;
}
