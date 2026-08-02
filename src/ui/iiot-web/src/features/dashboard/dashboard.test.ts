import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import {
  createMemoryHistory,
  createRouter,
  type LocationQueryRaw,
} from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { i18n } from '../../i18n';
import mainLayoutSource from '../../layout/MainLayout.vue?raw';
import { getHourlyByDeviceApi, type HourlyCapacityItem } from '../capacity/api';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
} from '../device-logs/api';
import {
  getDeviceStatusSummaryApi,
  getScopedDeviceSelectApi,
  type ScopedDeviceSelectDto,
} from '../devices/api';
import DashboardPage from './DashboardPage.vue';
import { resolveDashboardLoadError } from './errors';
import { dashboardRoutes } from './routes';
import {
  formatEventTime,
  mapDashboardEvent,
  toEventLabel,
  toEventSeverity,
  todayIsoDate,
} from './types';

vi.mock('../capacity/api', () => ({ getHourlyByDeviceApi: vi.fn() }));
vi.mock('../device-logs/api', () => ({
  getRecentAlertCountApi: vi.fn(),
  getRecentDeviceLogsApi: vi.fn(),
}));
vi.mock('../devices/api', () => ({
  getDeviceStatusSummaryApi: vi.fn(),
  getScopedDeviceSelectApi: vi.fn(),
}));

const getScopedDevices = vi.mocked(getScopedDeviceSelectApi);
const getStatus = vi.mocked(getDeviceStatusSummaryApi);
const getHourly = vi.mocked(getHourlyByDeviceApi);
const getAlertCount = vi.mocked(getRecentAlertCountApi);
const getRecentLogs = vi.mocked(getRecentDeviceLogsApi);

const processAId = '11111111-1111-1111-1111-111111111111';
const processBId = '22222222-2222-2222-2222-222222222222';
const deviceAId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const deviceBId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const deviceCId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

const scopedDevices: ScopedDeviceSelectDto[] = [
  {
    id: deviceAId,
    deviceName: '一号装配客户端',
    code: 'ASM-01',
    processId: processAId,
    processCode: 'ASM',
    processName: '装配',
  },
  {
    id: deviceBId,
    deviceName: '二号装配客户端',
    code: 'ASM-02',
    processId: processAId,
    processCode: 'ASM',
    processName: '装配',
  },
  {
    id: deviceCId,
    deviceName: '检测客户端',
    code: 'QC-01',
    processId: processBId,
    processCode: 'QC',
    processName: '检测',
  },
];

const emptyStatus = {
  total: 1,
  online: 0,
  warning: 0,
  error: 0,
  offline: 1,
  generatedAt: '2026-07-11T00:00:00Z',
  softwareStatus: 'MissingRuntimeHeartbeat',
  issue: '尚未收到客户端运行心跳' as string | null,
};

const emptyAlertSummary = {
  count: 0,
  sinceHours: 24,
  minLevel: 'WARN',
  windowStart: '2026-07-10T00:00:00Z',
  windowEnd: '2026-07-11T00:00:00Z',
  generatedAt: '2026-07-11T00:00:00Z',
};

function hourlySlot(
  totalCount: number,
  okCount: number,
  timeLabel = '08:00',
  plcCode = 'P1-AP01',
): HourlyCapacityItem {
  const [hour, minute] = timeLabel.split(':').map(Number);
  return {
    hour: hour ?? 0,
    minute: minute ?? 0,
    timeLabel,
    shiftCode: 'DAY',
    totalCount,
    okCount,
    ngCount: totalCount - okCount,
    plcCode,
    plcName: null,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

async function mountDashboard(query: LocationQueryRaw = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: DashboardPage }],
  });
  await router.push({ path: '/', query });
  await router.isReady();
  const wrapper = mount(DashboardPage, {
    global: {
      plugins: [createPinia(), i18n, router],
    },
  });
  return { wrapper, router };
}

async function selectContext(
  wrapper: VueWrapper,
  processId = processAId,
  deviceId = deviceAId,
) {
  await wrapper.get('[data-testid="dashboard-process-select"]').setValue(processId);
  await flushPromises();
  await wrapper.get('[data-testid="dashboard-device-select"]').setValue(deviceId);
  await flushPromises();
}

describe('dashboard feature', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    i18n.global.locale.value = 'zh-CN';
    getScopedDevices.mockResolvedValue(scopedDevices);
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

  it('keeps date and log formatting deterministic', () => {
    expect(todayIsoDate(new Date('2026-06-24T08:00:00+08:00'))).toBe('2026-06-24');
    expect(toEventSeverity('ERROR')).toBe('error');
    expect(toEventSeverity('WARNING')).toBe('warn');
    expect(toEventLabel('INFORMATION')).toBe('INFO');
    expect(formatEventTime('not-a-date', 'zh-CN')).toBe('--:--:--');
  });

  it('maps recent logs without inventing a device identity', () => {
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
  });

  it('loads only the authorized device context before a device is selected', async () => {
    const { wrapper } = await mountDashboard();
    await flushPromises();

    expect(getScopedDevices).toHaveBeenCalledTimes(1);
    expect(wrapper.find('[data-testid="dashboard-selection-required"]').exists()).toBe(true);
    expect(getStatus).not.toHaveBeenCalled();
    expect(getHourly).not.toHaveBeenCalled();
    expect(getAlertCount).not.toHaveBeenCalled();
    expect(getRecentLogs).not.toHaveBeenCalled();
  });

  it('shows context loading and context errors without starting metric requests', async () => {
    getScopedDevices.mockReturnValueOnce(new Promise<never>(() => {}));
    const { wrapper } = await mountDashboard();

    expect(wrapper.find('[data-testid="dashboard-context-loading"]').exists()).toBe(true);
    expect(getStatus).not.toHaveBeenCalled();

    wrapper.unmount();
    getScopedDevices.mockRejectedValueOnce(new Error('connection string secret'));
    const failedMount = await mountDashboard();
    await flushPromises();

    expect(
      failedMount.wrapper.find('[data-testid="dashboard-context-error"]').exists(),
    ).toBe(true);
    expect(failedMount.wrapper.text()).toContain('网络请求失败，请检查服务状态后重试');
    expect(failedMount.wrapper.text()).not.toContain('connection string secret');
    expect(getStatus).not.toHaveBeenCalled();
  });

  it('shows an explicit state when the user has no authorized devices', async () => {
    getScopedDevices.mockResolvedValueOnce([]);
    const { wrapper } = await mountDashboard();
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-no-devices"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('当前账号没有授权设备');
    expect(getStatus).not.toHaveBeenCalled();
  });

  it('filters devices by process and sends the same DeviceId to every source', async () => {
    getStatus.mockResolvedValueOnce({
      ...emptyStatus,
      online: 1,
      offline: 0,
      softwareStatus: 'Running',
      issue: null,
    });
    getHourly.mockResolvedValueOnce([hourlySlot(10, 9)]);
    const { wrapper, router } = await mountDashboard();
    await flushPromises();

    await wrapper.get('[data-testid="dashboard-process-select"]').setValue(processAId);
    await flushPromises();
    const deviceSelect = wrapper.get<HTMLSelectElement>(
      '[data-testid="dashboard-device-select"]',
    );
    expect(deviceSelect.findAll('option').map((option) => option.text())).toEqual([
      '请选择设备',
      '一号装配客户端 · ASM-01',
      '二号装配客户端 · ASM-02',
    ]);
    expect(getStatus).not.toHaveBeenCalled();

    await deviceSelect.setValue(deviceAId);
    await flushPromises();

    expect(router.currentRoute.value.query).toMatchObject({
      processId: processAId,
      deviceId: deviceAId,
    });
    expect(getStatus).toHaveBeenCalledWith({ deviceId: deviceAId });
    expect(getHourly).toHaveBeenCalledWith({ deviceId: deviceAId });
    expect(getAlertCount).toHaveBeenCalledWith({ deviceId: deviceAId });
    expect(getRecentLogs).toHaveBeenCalledWith({
      limit: 20,
      minLevel: 'WARN',
      deviceId: deviceAId,
    });
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('10');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('运行中');
    expect(wrapper.text()).toContain('装配 · 一号装配客户端');
  });

  it('restores a valid URL context and clears an invalid or lost-access pair', async () => {
    const validMount = await mountDashboard({
      processId: processAId,
      deviceId: deviceBId,
    });
    await flushPromises();

    expect(getStatus).toHaveBeenCalledWith({ deviceId: deviceBId });
    expect(
      validMount.wrapper.get<HTMLSelectElement>(
        '[data-testid="dashboard-device-select"]',
      ).element.value,
    ).toBe(deviceBId);

    vi.clearAllMocks();
    getScopedDevices.mockResolvedValue(scopedDevices);
    getStatus.mockResolvedValue(emptyStatus);
    getHourly.mockResolvedValue([]);
    getAlertCount.mockResolvedValue(emptyAlertSummary);
    getRecentLogs.mockResolvedValue([]);
    const invalidMount = await mountDashboard({
      processId: processBId,
      deviceId: deviceAId,
    });
    await flushPromises();

    expect(invalidMount.router.currentRoute.value.query.processId).toBeUndefined();
    expect(invalidMount.router.currentRoute.value.query.deviceId).toBeUndefined();
    expect(
      invalidMount.wrapper.find('[data-testid="dashboard-selection-required"]').exists(),
    ).toBe(true);
    expect(getStatus).not.toHaveBeenCalled();
  });

  it('clears the selected device and old metrics when the process changes', async () => {
    getHourly.mockResolvedValueOnce([hourlySlot(18, 18)]);
    const { wrapper, router } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('18');

    await wrapper.get('[data-testid="dashboard-process-select"]').setValue(processBId);
    await flushPromises();

    expect(wrapper.find('[data-testid="dashboard-selection-required"]').exists()).toBe(true);
    expect(
      wrapper.get<HTMLSelectElement>('[data-testid="dashboard-device-select"]').element.value,
    ).toBe('');
    expect(router.currentRoute.value.query).toEqual({ processId: processBId });
    expect(getHourly).toHaveBeenCalledTimes(1);
  });

  it('renders successful empty responses as zero and keeps generic output semantics', async () => {
    const { wrapper } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper);

    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="dashboard-trend-empty"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('0');
    expect(wrapper.get('[data-testid="dashboard-card-reporting-plcs-value"]').text()).toBe('0');
    expect(wrapper.get('[data-testid="dashboard-production-display"]').text()).toBe('0');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('无运行心跳');
    expect(wrapper.text()).toContain('今日完工弹夹数');
    expect(wrapper.text()).toContain('今日有产能上报的 PLC 数');
    expect(wrapper.text()).toContain('不等同于在线 PLC 数');
    expect(wrapper.text()).not.toContain('合格率');
  });

  it('keeps successful sources visible when one selected-device source fails', async () => {
    getStatus.mockRejectedValueOnce({
      isAxiosError: true,
      response: {
        status: 403,
        data: { detail: '当前账号无权读取客户端运行状态' },
        headers: { 'content-type': 'application/problem+json' },
      },
    });
    getHourly.mockResolvedValueOnce([hourlySlot(10, 9)]);
    const { wrapper } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper);

    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="dashboard-error"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('10');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('--');
    expect(
      wrapper.get('[data-testid="dashboard-card-active-clients"]')
        .attributes('data-source-status'),
    ).toBe('error');
    expect(wrapper.text()).toContain('当前账号无权读取客户端运行状态');
    expect(wrapper.find('[data-testid="dashboard-status-error"]').exists()).toBe(true);
  });

  it('renders a safe global retry only when every selected-device source fails', async () => {
    const unsafeError = new Error('database secret must not be shown');
    getStatus.mockRejectedValueOnce(unsafeError);
    getHourly.mockRejectedValueOnce(unsafeError);
    getAlertCount.mockRejectedValueOnce(unsafeError);
    getRecentLogs.mockRejectedValueOnce(unsafeError);
    const { wrapper } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper);

    expect(wrapper.find('[data-testid="dashboard-error"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('网络请求失败，请检查服务状态后重试');
    expect(wrapper.text()).not.toContain('database secret');

    await wrapper.get('[data-testid="dashboard-error"] button').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="dashboard-ready"]').exists()).toBe(true);
  });

  it('preserves safe API messages but sanitizes generic errors', async () => {
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

  it('derives the single-device status and aggregates equal time buckets', async () => {
    getStatus.mockResolvedValueOnce({
      ...emptyStatus,
      warning: 1,
      offline: 0,
      softwareStatus: 'Starting',
      issue: null,
    });
    getHourly.mockResolvedValueOnce([
      hourlySlot(10, 9),
      hourlySlot(5, 4, '08:00', 'P1-AP02'),
    ]);
    getAlertCount.mockResolvedValueOnce({ ...emptyAlertSummary, count: 7 });
    getRecentLogs.mockResolvedValueOnce([{
      id: 'log-1',
      deviceId: deviceAId,
      deviceName: '一号装配客户端',
      level: 'WARN',
      message: '真实接口告警',
      logTime: '2026-07-11T08:00:00Z',
      receivedAt: '2026-07-11T08:00:01Z',
    }]);
    const { wrapper } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper);

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('15');
    expect(wrapper.get('[data-testid="dashboard-card-reporting-plcs-value"]').text()).toBe('2');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('启动中');
    expect(wrapper.get('[data-testid="dashboard-card-alert-records-value"]').text()).toBe('7');
    expect(wrapper.findAll('.dashboard-bars__bar')).toHaveLength(1);
    expect(wrapper.get('.dashboard-bars__bar').attributes('title')).toBe('08:00: 15');
    expect(wrapper.text()).toContain('真实接口告警');
  });

  it('ignores stale responses after the selected device changes', async () => {
    const staleStatus = deferred<typeof emptyStatus>();
    const staleHourly = deferred<Awaited<ReturnType<typeof getHourlyByDeviceApi>>>();
    getStatus.mockReturnValueOnce(staleStatus.promise);
    getHourly.mockReturnValueOnce(staleHourly.promise);
    const { wrapper } = await mountDashboard();
    await flushPromises();
    await selectContext(wrapper, processAId, deviceAId);

    getStatus.mockResolvedValueOnce({
      ...emptyStatus,
      warning: 1,
      offline: 0,
      softwareStatus: 'Starting',
      issue: null,
    });
    getHourly.mockResolvedValueOnce([hourlySlot(20, 20, '09:30')]);
    await wrapper.get('[data-testid="dashboard-device-select"]').setValue(deviceBId);
    await flushPromises();

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('20');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('启动中');

    staleStatus.resolve({
      ...emptyStatus,
      online: 1,
      offline: 0,
      softwareStatus: 'Running',
      issue: null,
    });
    staleHourly.resolve([hourlySlot(999, 999, '10:00')]);
    await flushPromises();

    expect(wrapper.get('[data-testid="dashboard-card-production-value"]').text()).toBe('20');
    expect(wrapper.get('[data-testid="dashboard-card-active-clients-value"]').text()).toBe('启动中');
  });
});
