import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { createMemoryHistory, createRouter, type LocationQueryRaw } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { i18n } from '../../i18n';
import { getScopedDeviceSelectApi } from '../devices/api';
import CapacityDashboardPage from './CapacityDashboardPage.vue';
import CapacityDetailPage from './CapacityDetailPage.vue';
import {
  getDailyPagedApi,
  getHourlyByDeviceApi,
  getSummaryRangeApi,
} from './api';

vi.mock('../devices/api', () => ({ getScopedDeviceSelectApi: vi.fn() }));
vi.mock('./api', () => ({
  getDailyPagedApi: vi.fn(),
  getHourlyByDeviceApi: vi.fn(),
  getSummaryRangeApi: vi.fn(),
}));

const getScopedDevices = vi.mocked(getScopedDeviceSelectApi);
const getDaily = vi.mocked(getDailyPagedApi);
const getHourly = vi.mocked(getHourlyByDeviceApi);
const getSummary = vi.mocked(getSummaryRangeApi);
const processAId = '11111111-1111-1111-1111-111111111111';
const processBId = '22222222-2222-2222-2222-222222222222';
const deviceAId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const deviceBId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function emptyDailyPage(): Awaited<ReturnType<typeof getDailyPagedApi>> {
  return {
    items: [],
    metaData: { totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

async function mountCapacity(
  component: typeof CapacityDashboardPage | typeof CapacityDetailPage,
  path: string,
  query: LocationQueryRaw = {},
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/capacity', name: 'CapacityDashboard', component: CapacityDashboardPage },
      { path: '/capacity/detail', name: 'CapacityDetail', component: CapacityDetailPage },
    ],
  });
  await router.push({ path, query });
  await router.isReady();
  const wrapper = mount(component, {
    global: { plugins: [createPinia(), i18n, router] },
  });
  return { wrapper, router };
}

describe('capacity production context flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    i18n.global.locale.value = 'zh-CN';
    getScopedDevices.mockResolvedValue([
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
        deviceName: '检测客户端',
        code: 'QC-01',
        processId: processBId,
        processCode: 'QC',
        processName: '检测',
      },
    ]);
    getDaily.mockResolvedValue(emptyDailyPage());
    getHourly.mockResolvedValue([]);
    getSummary.mockResolvedValue([]);
  });

  it('does not request capacity before process and device are both selected', async () => {
    const { wrapper } = await mountCapacity(CapacityDashboardPage, '/capacity');
    await flushPromises();

    expect(wrapper.find('[data-testid="capacity-select-process"]').exists()).toBe(true);
    expect(getDaily).not.toHaveBeenCalled();

    await wrapper.get('[data-testid="capacity-process-select"]').setValue(processAId);
    await flushPromises();
    expect(wrapper.find('[data-testid="capacity-select-device"]').exists()).toBe(true);
    expect(wrapper.get<HTMLSelectElement>('[data-testid="capacity-device-select"]')
      .findAll('option').map((option) => option.text())).toEqual([
      '请选择设备',
      '一号装配客户端 · ASM-01',
    ]);
    expect(getDaily).not.toHaveBeenCalled();
  });

  it('automatically loads only the selected device and carries both IDs to detail', async () => {
    getDaily.mockResolvedValueOnce({
      items: [{
        deviceId: deviceAId,
        deviceName: '一号装配客户端',
        date: '2026-08-02',
        totalCount: 12,
        okCount: 11,
        ngCount: 1,
        okRate: 11 / 12,
        reportedAt: '2026-08-02T08:00:00Z',
      }],
      metaData: { totalCount: 1, pageSize: 10, currentPage: 1, totalPages: 1 },
    });
    const { wrapper, router } = await mountCapacity(CapacityDashboardPage, '/capacity');
    await flushPromises();
    await wrapper.get('[data-testid="capacity-process-select"]').setValue(processAId);
    await wrapper.get('[data-testid="capacity-device-select"]').setValue(deviceAId);
    await flushPromises();

    expect(getDaily).toHaveBeenCalledTimes(1);
    expect(getDaily).toHaveBeenCalledWith(expect.objectContaining({ deviceId: deviceAId }));
    expect(wrapper.text()).toContain('12');

    const detailButton = wrapper.findAll('button').find((button) => button.text() === '查看详情');
    expect(detailButton).toBeDefined();
    await detailButton!.trigger('click');
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      processId: processAId,
      deviceId: deviceAId,
    });
  });

  it('clears a cross-process URL pair and never starts a request', async () => {
    const { wrapper, router } = await mountCapacity(CapacityDashboardPage, '/capacity', {
      processId: processBId,
      deviceId: deviceAId,
    });
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({});
    expect(wrapper.find('[data-testid="capacity-select-process"]').exists()).toBe(true);
    expect(getDaily).not.toHaveBeenCalled();
  });

  it('invalidates a late capacity response when the process changes', async () => {
    const late = deferred<ReturnType<typeof emptyDailyPage>>();
    getDaily.mockReturnValueOnce(late.promise);
    const { wrapper } = await mountCapacity(CapacityDashboardPage, '/capacity');
    await flushPromises();
    await wrapper.get('[data-testid="capacity-process-select"]').setValue(processAId);
    await wrapper.get('[data-testid="capacity-device-select"]').setValue(deviceAId);

    await wrapper.get('[data-testid="capacity-process-select"]').setValue(processBId);
    await flushPromises();
    late.resolve({
      items: [{
        deviceId: deviceAId,
        deviceName: '旧设备',
        date: '2026-08-02',
        totalCount: 999,
        okCount: 999,
        ngCount: 0,
        okRate: 1,
        reportedAt: '2026-08-02T08:00:00Z',
      }],
      metaData: { totalCount: 1, pageSize: 10, currentPage: 3, totalPages: 3 },
    });
    await flushPromises();

    expect(wrapper.find('[data-testid="capacity-select-device"]').exists()).toBe(true);
    expect(wrapper.text()).not.toContain('999');
  });

  it('shows a 403 message and retries the selected-device request', async () => {
    getDaily.mockRejectedValueOnce({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['当前账号无权读取该设备产能'],
    });
    const { wrapper } = await mountCapacity(CapacityDashboardPage, '/capacity', {
      processId: processAId,
      deviceId: deviceAId,
    });
    await flushPromises();

    expect(wrapper.text()).toContain('当前账号无权读取该设备产能');
    await wrapper.get('.capacity-page__table-card button').trigger('click');
    await flushPromises();
    expect(getDaily).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain('当前设备暂无产能数据');
  });

  it('validates detail URL context before requesting hourly data', async () => {
    const invalid = await mountCapacity(CapacityDetailPage, '/capacity/detail', {
      processId: processBId,
      deviceId: deviceAId,
    });
    await flushPromises();
    expect(invalid.wrapper.find('[data-testid="capacity-detail-select-process"]').exists()).toBe(true);
    expect(getHourly).not.toHaveBeenCalled();
    invalid.wrapper.unmount();

    const valid = await mountCapacity(CapacityDetailPage, '/capacity/detail', {
      processId: processAId,
      deviceId: deviceAId,
    });
    await flushPromises();
    expect(getHourly).toHaveBeenCalledWith(expect.objectContaining({ deviceId: deviceAId }));
    expect(valid.wrapper.text()).toContain('一号装配客户端');
  });
});
