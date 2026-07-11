import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import {
  createMemoryHistory,
  createRouter,
  type Router,
} from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthRequestError, loginApi } from '../../api/auth';
import { i18n } from '../../i18n';
import { useAuthStore } from '../../stores/auth';
import LoginAccessPanel from './LoginAccessPanel.vue';
import LoginCapabilityPanel from './LoginCapabilityPanel.vue';
import { getSafeLoginReturnUrl, navigateAfterLogin } from './loginNavigation';

vi.mock('../../api/auth', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/auth')>();
  return { ...actual, loginApi: vi.fn() };
});

const loginApiMock = vi.mocked(loginApi);
const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: '2026-07-12T00:00:00Z',
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: '2026-07-18T00:00:00Z',
};

async function mountAccessPanel(returnUrl?: string) {
  const pinia = createPinia();
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
      { path: '/devices', component: { template: '<div />' } },
    ],
  });
  await router.push({ path: '/login', query: returnUrl ? { returnUrl } : undefined });
  await router.isReady();
  const wrapper = mount(LoginAccessPanel, {
    global: { plugins: [pinia, router, i18n] },
  });
  const authStore = useAuthStore(pinia);
  const setSession = vi.spyOn(authStore, 'setSession').mockImplementation(() => {});
  return { wrapper, router, setSession };
}

function loginButton(wrapper: Awaited<ReturnType<typeof mountAccessPanel>>['wrapper']) {
  const button = wrapper.findAll('button').find((item) => item.text() === '登录');
  if (!button) throw new Error('Login button not found.');
  return button;
}

describe('login feature', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    i18n.global.locale.value = 'zh-CN';
  });

  it('describes product capabilities without unauthenticated runtime data', () => {
    const wrapper = mount(LoginCapabilityPanel, {
      global: { plugins: [i18n] },
    });

    expect(wrapper.text()).toContain('登录后查看真实生产运营信息');
    expect(wrapper.text()).toContain('未登录页面不读取或推测设备、产量、告警和同步状态');
    expect(wrapper.text()).not.toMatch(/36|932|98\.6%|车间在线|数据同步正常|网关刚刚上报|实时/);
  });

  it('keeps the English public screen free of live production claims', () => {
    i18n.global.locale.value = 'en-US';
    const wrapper = mount(LoginCapabilityPanel, {
      global: { plugins: [i18n] },
    });

    expect(wrapper.text()).toContain('Sign in to review real production operations');
    expect(wrapper.text()).not.toMatch(/Workshop online|Data sync normal|Gateway just reported|98\.6%|\b932\b/);
  });

  it('keeps required-field validation in the extracted access panel', async () => {
    const { wrapper } = await mountAccessPanel();
    await loginButton(wrapper).trigger('click');

    expect(wrapper.text()).toContain('请输入工号和密码');
    expect(loginApiMock).not.toHaveBeenCalled();
  });

  it('submits to the real login boundary and honors a safe local return URL', async () => {
    loginApiMock.mockResolvedValue(session);
    const { wrapper, router, setSession } = await mountAccessPanel('/devices');
    await wrapper.get('input[type="text"]').setValue('E1001');
    await wrapper.get('input[type="password"]').setValue('correct-password');
    await loginButton(wrapper).trigger('click');
    await flushPromises();

    expect(loginApiMock).toHaveBeenCalledWith({ employeeNo: 'E1001', password: 'correct-password' });
    expect(setSession).toHaveBeenCalledWith(session);
    expect(router.currentRoute.value.fullPath).toBe('/devices');
  });

  it('shows a safe classified authentication error without raw details', async () => {
    loginApiMock.mockRejectedValue(new AuthRequestError('network', 'raw network detail'));
    const { wrapper } = await mountAccessPanel();
    await wrapper.get('input[type="text"]').setValue('E1001');
    await wrapper.get('input[type="password"]').setValue('password');
    await loginButton(wrapper).trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('无法连接云端服务');
    expect(wrapper.text()).not.toContain('raw network detail');
    expect(wrapper.find('[role="alert"]').exists()).toBe(true);
  });

  it('rejects external return URLs and preserves the OIDC connect redirect branch', async () => {
    expect(getSafeLoginReturnUrl('//evil.example/path')).toBeNull();
    expect(getSafeLoginReturnUrl('/devices\\redirect')).toBeNull();
    expect(getSafeLoginReturnUrl('/connect/authorize?client_id=iiot-web')).toBe('/connect/authorize?client_id=iiot-web');

    const router = { push: vi.fn() } as unknown as Router;
    const assign = vi.fn();
    await navigateAfterLogin(router, '/connect/authorize?client_id=iiot-web', assign);
    expect(assign).toHaveBeenCalledWith('/connect/authorize?client_id=iiot-web');
    expect(router.push).not.toHaveBeenCalled();
  });
});
