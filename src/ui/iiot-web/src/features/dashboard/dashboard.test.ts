import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { i18n } from '../../i18n';
import mainLayoutSource from '../../layout/MainLayout.vue?raw';
import { getHourlyAggregateApi } from '../capacity/api';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
} from '../device-logs/api';
import { getDeviceStatusSummaryApi } from '../devices/api';
import DashboardPage from './DashboardPage.vue';
import { resolveDashboardLoadError } from './errors';
import { dashboardRoutes } from './routes';
import {
  formatEventTime,
  hasDashboardData,
  mapDashboardEvent,
  toEventLabel,
  toEventSeverity,
  todayIsoDate,
} from './types';

vi.mock('../capacity/api', () => ({ getHourlyAggregateApi: vi.fn() }));
vi.mock('../device-logs/api', () => ({
  getRecentAlertCountApi: vi.fn(),
  getRecentDeviceLogsApi: vi.fn(),
}));
vi.mock('../devices/api', () => ({ getDeviceStatusSummaryApi: vi.fn() }));

const getStatus = vi.mocked(getDeviceStatusSummaryApi);
const getHourly = vi.mocked(getHourlyAggregateApi);
const getAlertCount = vi.mocked(getRecentAlertCountApi);
const getRecentLogs = vi.mocked(getRecentDeviceLogsApi);

const emptyStatus = {
  total: 0,
  online: 0,
  warning: 0,
  error: 0,
  offline: 0,
  generatedAt: '2026-07-11T00:00:00Z',
};

const emptyAlertSummary = {
  count: 0,
  sinceHours: 24,
  minLevel: 'WARN',
  windowStart: '2026-07-10T00:00:00Z',
  windowEnd: '2026-07-11T00:00:00Z',
  generatedAt: '2026-07-11T00:00:00Z',
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

function mountDashboard() {
  return mount(DashboardPage, {
    global: {
      plugins: [createPinia(), i18n],
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  });
}

describe('dashboard feature', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    i18n.global.locale.value = 'zh-CN';
    getStatus.mockResolvedValue(emptyStatus);
    getHourly.mockResolvedValue([]);
    getAlertCount.mockResolvedValue(emptyAlertSummary);
    getRecentLogs.mockResolvedValue([]);
  });

  it('keeps the dashboard route as the default authenticated child route', () => {
    expect(dashboardRoutes[0]!.path).toBe('');
    expect(dashboardRoutes[0]!.name).toBe('Dashboard');
    expect(dashboardRoutes[0]!.meta?.requiresAuth).toBe(true);
  });

  it('does not present an unverified global data-sync badge', () => {
    expect(mainLayoutSource).not.toContain('common.dataSyncNormal');
    expect(mainLayoutSource).not.toContain('数据同步正常');
  });

  it('formats current day for capacity aggregate API calls', () => {
    expect(todayIsoDate(new Date('2026-06-24T08:00:00+08:00'))).toBe('2026-06-24');
  });

  it('normalizes alert labels and severities', () => {
    expect(toEventSeverity('ERROR')).toBe('error');
    expect(toEventSeverity('WARNING')).toBe('warn');
    expect(toEventSeverity('INFO')).toBe('info');
    expect(toEventLabel('INFORMATION')).toBe('INFO');
  });

  it('maps recent logs without inventing data', () => {
    const event = mapDashboardEvent({
      id: '1',
      deviceId: 'device-abcdef',
      deviceName: '',
      level: 'WARN',
      message: '温度告警',
      logTime: '2026-06-24T10:10:10Z',
      receivedAt: '2026-06-24T10:10:12Z',
    }, 'zh-CN');
    expect(event.deviceCode).toBe('device-a');
    expect(event.label).toBe('WARN');
    expect(formatEventTime('not-a-date', 'zh-CN')).toBe('--:--:--');
  });

  it('distinguishes real data presence from an empty dashboard', () => {
    expect(hasDashboardData({
      totalDevices: 0,
      hourlyCount: 0,
      alertCount: 0,
      eventCount: 0,
    })).toBe(false);
    expect(hasDashboardData({
      totalDevices: 1,
      hourlyCount: 0,
      alertCount: 0,
      eventCount: 0,
    })).toBe(true);
  });

  it('renders loading before any scoped API finishes', () => {
    const pending = new Promise<never>(() => {});
    getStatus.mockReturnValue(pending);
    getHourly.mockReturnValue(pending);
    getAlertCount.mockReturnValue(pending);
    getRecentLogs.mockReturnValue(pending);

    const wrapper = mountDashboard();
    expect(wrapper.find('[data-testid="dashboard-loading"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(false);
  });

  it('renders a true empty state when all scoped sources are empty', async () => {
    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-empty"]').exists()).toBe(true);
    expect(wrapper.text()).toContain(
      '当前权限范围内没有 Cloud 客户端、今日产能或 WARN/ERROR 日志记录',
    );
    expect(wrapper.text()).not.toMatch(/周日|甲班组|质检岗|维修岗/);
  });

  it('keeps successful sources visible when one source fails', async () => {
    getStatus.mockRejectedValueOnce({
      isAxiosError: true,
      response: {
        status: 503,
        data: { detail: 'Cloud 客户端状态服务暂不可用' },
        headers: { 'content-type': 'application/problem+json' },
      },
    });
    getHourly.mockResolvedValueOnce([{
      hour: 8,
      minute: 0,
      timeLabel: '08:00',
      totalCount: 10,
      okCount: 9,
      ngCount: 1,
    }]);

    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="dashboard-error"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('10');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('--');
    expect(
      wrapper.get('[data-testid="dashboard-card-active-clients"]')
        .attributes('data-source-status'),
    ).toBe('error');
    expect(wrapper.text()).toContain('Cloud 客户端状态服务暂不可用');
    expect(wrapper.find('[data-testid="dashboard-status-error"]').exists()).toBe(true);
  });

  it('renders a safe global retry state only when all sources fail', async () => {
    const unsafeError = new Error('database secret must not be shown');
    getStatus.mockRejectedValueOnce(unsafeError);
    getHourly.mockRejectedValueOnce(unsafeError);
    getAlertCount.mockRejectedValueOnce(unsafeError);
    getRecentLogs.mockRejectedValueOnce(unsafeError);

    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-error"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('网络请求失败，请检查服务状态后重试');
    expect(wrapper.text()).not.toContain('database secret');

    await wrapper.get('button').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="dashboard-empty"]').exists()).toBe(true);
  });

  it('preserves ProblemDetails and ApiResult messages but sanitizes generic errors', async () => {
    await expect(resolveDashboardLoadError({
      isAxiosError: true,
      response: {
        status: 400,
        data: { detail: '设备权限范围无效' },
        headers: { 'content-type': 'application/problem+json' },
      },
    })).resolves.toBe('设备权限范围无效');
    await expect(resolveDashboardLoadError({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['当前账号不可读取产能'],
    })).resolves.toBe('当前账号不可读取产能');
    await expect(resolveDashboardLoadError(
      new Error('internal connection string'),
    )).resolves.toBe('网络请求失败，请检查服务状态后重试。');
  });

  it('renders only scoped API values and keeps Cloud status semantics explicit', async () => {
    getStatus.mockResolvedValue({
      total: 5,
      online: 1,
      warning: 1,
      error: 1,
      offline: 2,
      generatedAt: '2026-07-11T00:00:00Z',
    });
    getHourly.mockResolvedValue([{
      hour: 8,
      minute: 0,
      timeLabel: '08:00',
      totalCount: 10,
      okCount: 9,
      ngCount: 1,
    }]);
    getAlertCount.mockResolvedValue({
      ...emptyAlertSummary,
      count: 7,
    });
    getRecentLogs.mockResolvedValue([{
      id: 'log-1',
      deviceId: 'device-abcdef',
      deviceName: '测试设备',
      level: 'WARN',
      message: '真实接口告警',
      logTime: '2026-07-11T08:00:00Z',
      receivedAt: '2026-07-11T08:00:01Z',
    }]);

    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('10');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('3');
    expect(wrapper.get('[data-testid="dashboard-card-pass-rate-value"]').text()).toBe('90.0%');
    expect(wrapper.get('[data-testid="dashboard-card-alert-records-value"]').text()).toBe('7');
    expect(wrapper.text()).toContain('原始日志条数，未去重');
    expect(wrapper.text()).toContain('Cloud 接收活动 · 3 / 5');
    expect(wrapper.text()).toContain(
      '正常、预警和故障均表示近 60 分钟有 Cloud 接收活动',
    );
    expect(wrapper.text()).toContain('真实接口告警');
    expect(wrapper.get('.dashboard-bars__bar').attributes('title')).toBe('08:00: 10');
    expect(wrapper.text()).not.toMatch(/周日|甲班组|质检岗|维修岗/);
  });

  it('uses source-specific empty states instead of fabricated values', async () => {
    getStatus.mockResolvedValue({ ...emptyStatus, total: 1, offline: 1 });
    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="dashboard-trend-empty"]').exists()).toBe(true);
    expect(wrapper.findAll('.dashboard-bars__bar')).toHaveLength(0);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('--');
    expect(wrapper.get('[data-testid="dashboard-card-pass-rate-value"]').text()).toBe('--');
    expect(wrapper.get('[data-testid="dashboard-production-display"]').text()).toBe('--');
    expect(wrapper.text()).not.toContain('0.0%');
  });

  it('shows true zero production only when the API returns an hourly slot', async () => {
    getStatus.mockResolvedValue({ ...emptyStatus, total: 1, offline: 1 });
    getHourly.mockResolvedValue([{
      hour: 8,
      minute: 0,
      timeLabel: '08:00',
      totalCount: 0,
      okCount: 0,
      ngCount: 0,
    }]);
    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('0');
    expect(wrapper.get('[data-testid="dashboard-card-pass-rate-value"]').text()).toBe('0.0%');
    expect(wrapper.get('[data-testid="dashboard-production-display"]').text()).toBe('0');
    expect(wrapper.get('.dashboard-bars__bar').attributes('style')).toContain('height: 0%');
  });

  it('ignores stale responses after a newer dashboard load starts', async () => {
    const wrapper = mountDashboard();
    await flushPromises();

    const staleStatus = deferred<typeof emptyStatus>();
    const staleHourly = deferred<Awaited<ReturnType<typeof getHourlyAggregateApi>>>();
    getStatus.mockReturnValueOnce(staleStatus.promise);
    getHourly.mockReturnValueOnce(staleHourly.promise);

    const page = wrapper.vm as unknown as { loadDashboard: () => Promise<void> };
    const staleLoad = page.loadDashboard();
    await flushPromises();

    getStatus.mockResolvedValueOnce({
      ...emptyStatus,
      total: 2,
      online: 1,
      warning: 1,
    });
    getHourly.mockResolvedValueOnce([{
      hour: 9,
      minute: 30,
      timeLabel: '09:30',
      totalCount: 20,
      okCount: 20,
      ngCount: 0,
    }]);
    await page.loadDashboard();
    await flushPromises();

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('20');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('2');

    staleStatus.resolve({
      ...emptyStatus,
      total: 99,
      online: 99,
    });
    staleHourly.resolve([{
      hour: 10,
      minute: 0,
      timeLabel: '10:00',
      totalCount: 999,
      okCount: 999,
      ngCount: 0,
    }]);
    await staleLoad;
    await flushPromises();

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('20');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('2');
  });
});
