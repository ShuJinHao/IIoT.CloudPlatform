import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { i18n } from '../../i18n';
import { getHourlyAggregateApi } from '../capacity/api';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
} from '../device-logs/api';
import { getDeviceStatusSummaryApi } from '../devices/api';
import DashboardPage from './DashboardPage.vue';
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
    getAlertCount.mockResolvedValue({
      count: 0,
      sinceHours: 24,
      minLevel: 'WARN',
      windowStart: '2026-07-10T00:00:00Z',
      windowEnd: '2026-07-11T00:00:00Z',
      generatedAt: '2026-07-11T00:00:00Z',
    });
    getRecentLogs.mockResolvedValue([]);
  });

  it('keeps the dashboard route as the default authenticated child route', () => {
    expect(dashboardRoutes[0]!.path).toBe('');
    expect(dashboardRoutes[0]!.name).toBe('Dashboard');
    expect(dashboardRoutes[0]!.meta?.requiresAuth).toBe(true);
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
    expect(hasDashboardData({ totalDevices: 0, hourlyCount: 0, alertCount: 0, eventCount: 0 })).toBe(false);
    expect(hasDashboardData({ totalDevices: 1, hourlyCount: 0, alertCount: 0, eventCount: 0 })).toBe(true);
  });

  it('renders loading before the required scoped APIs finish', () => {
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
    expect(wrapper.text()).toContain('当前权限范围内没有设备、产能或告警记录');
    expect(wrapper.text()).not.toMatch(/周日|甲班组|质检岗|维修岗/);
  });

  it('renders a safe retryable error and clears failed data', async () => {
    getStatus.mockRejectedValueOnce(new Error('database secret must not be shown'));
    const wrapper = mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-error"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('无法读取当前账号权限范围内的数据');
    expect(wrapper.text()).not.toContain('database secret');

    await wrapper.get('button').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="dashboard-empty"]').exists()).toBe(true);
  });

  it('renders only values returned by the scoped APIs', async () => {
    getStatus.mockResolvedValue({
      total: 3,
      online: 2,
      warning: 1,
      error: 0,
      offline: 0,
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
      count: 1,
      sinceHours: 24,
      minLevel: 'WARN',
      windowStart: '2026-07-10T00:00:00Z',
      windowEnd: '2026-07-11T00:00:00Z',
      generatedAt: '2026-07-11T00:00:00Z',
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
    expect(wrapper.text()).toContain('90.0%');
    expect(wrapper.text()).toContain('真实接口告警');
    expect(wrapper.get('.dashboard-bars__bar').attributes('title')).toBe('08:00: 10');
    expect(wrapper.text()).not.toMatch(/周日|甲班组|质检岗|维修岗/);
  });

  it('uses a trend empty state instead of fabricated bars when only device data exists', async () => {
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
});
