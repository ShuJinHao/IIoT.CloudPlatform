import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { createMemoryHistory, createRouter, type LocationQueryRaw } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { i18n } from '../../i18n';
import { getScopedDeviceSelectApi } from '../devices/api';
import DeviceLogPage from './DeviceLogPage.vue';
import {
  getLogsByDeviceAndDateApi,
  getLogsByDeviceAndKeywordApi,
  getLogsByDeviceAndLevelApi,
  getLogsByDeviceAndTimeRangeApi,
  getLogsByDeviceDateAndKeywordApi,
} from './api';

vi.mock('../devices/api', () => ({ getScopedDeviceSelectApi: vi.fn() }));
vi.mock('./api', () => ({
  getLogsByDeviceAndDateApi: vi.fn(),
  getLogsByDeviceAndKeywordApi: vi.fn(),
  getLogsByDeviceAndLevelApi: vi.fn(),
  getLogsByDeviceAndTimeRangeApi: vi.fn(),
  getLogsByDeviceDateAndKeywordApi: vi.fn(),
}));
vi.mock('../../utils/feedback', () => ({ notifyWarning: vi.fn() }));

const getScopedDevices = vi.mocked(getScopedDeviceSelectApi);
const getByLevel = vi.mocked(getLogsByDeviceAndLevelApi);
const processAId = '11111111-1111-1111-1111-111111111111';
const processBId = '22222222-2222-2222-2222-222222222222';
const deviceAId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const deviceBId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function emptyLogPage(): Awaited<ReturnType<typeof getLogsByDeviceAndLevelApi>> {
  return {
    items: [],
    metaData: { totalCount: 0, pageSize: 20, currentPage: 1, totalPages: 1 },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

async function mountLogs(query: LocationQueryRaw = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/device-logs', component: DeviceLogPage }],
  });
  await router.push({ path: '/device-logs', query });
  await router.isReady();
  const wrapper = mount(DeviceLogPage, {
    global: { plugins: [createPinia(), i18n, router] },
  });
  return { wrapper, router };
}

describe('device log production context flow', () => {
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
    getByLevel.mockResolvedValue(emptyLogPage());
    vi.mocked(getLogsByDeviceAndDateApi).mockResolvedValue(emptyLogPage());
    vi.mocked(getLogsByDeviceAndKeywordApi).mockResolvedValue(emptyLogPage());
    vi.mocked(getLogsByDeviceAndTimeRangeApi).mockResolvedValue(emptyLogPage());
    vi.mocked(getLogsByDeviceDateAndKeywordApi).mockResolvedValue(emptyLogPage());
  });

  it('requires process then device and never loads history automatically', async () => {
    const { wrapper } = await mountLogs();
    await flushPromises();

    expect(wrapper.find('[data-testid="device-logs-select-process"]').exists()).toBe(true);
    expect(getByLevel).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="device-logs-process-select"]').setValue(processAId);
    await flushPromises();
    expect(wrapper.find('[data-testid="device-logs-select-device"]').exists()).toBe(true);
    expect(wrapper.get<HTMLSelectElement>('[data-testid="device-logs-device-select"]')
      .findAll('option').map((option) => option.text())).toEqual([
      '请选择设备',
      '一号装配客户端 · ASM-01',
    ]);

    await wrapper.get('[data-testid="device-logs-device-select"]').setValue(deviceAId);
    await flushPromises();
    expect(wrapper.text()).toContain('设置条件后点击查询');
    expect(getByLevel).not.toHaveBeenCalled();
  });

  it('queries only after a complete URL context and an explicit click', async () => {
    const { wrapper } = await mountLogs({ processId: processAId, deviceId: deviceAId });
    await flushPromises();
    expect(getByLevel).not.toHaveBeenCalled();

    const button = wrapper.findAll('button').find((item) => item.text() === '查询');
    expect(button).toBeDefined();
    await button!.trigger('click');
    await flushPromises();

    expect(getByLevel).toHaveBeenCalledWith(expect.objectContaining({
      deviceId: deviceAId,
      pagination: { PageNumber: 1, PageSize: 20 },
    }));
    expect(wrapper.text()).toContain('当前设备暂无日志数据');
  });

  it('clears an invalid cross-process URL without querying', async () => {
    const { wrapper, router } = await mountLogs({
      processId: processBId,
      deviceId: deviceAId,
    });
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({});
    expect(wrapper.find('[data-testid="device-logs-select-process"]').exists()).toBe(true);
    expect(getByLevel).not.toHaveBeenCalled();
  });

  it('clears results and ignores a late response after process change', async () => {
    const late = deferred<ReturnType<typeof emptyLogPage>>();
    getByLevel.mockReturnValueOnce(late.promise);
    const { wrapper } = await mountLogs({ processId: processAId, deviceId: deviceAId });
    await flushPromises();
    const button = wrapper.findAll('button').find((item) => item.text() === '查询');
    await button!.trigger('click');

    await wrapper.get('[data-testid="device-logs-process-select"]').setValue(processBId);
    await flushPromises();
    late.resolve({
      items: [{
        id: 'stale-log',
        deviceId: deviceAId,
        deviceName: '旧设备',
        level: 'ERROR',
        message: 'STALE LOG MUST NOT RENDER',
        logTime: '2026-08-02T08:00:00Z',
        receivedAt: '2026-08-02T08:00:01Z',
      }],
      metaData: { totalCount: 1, pageSize: 20, currentPage: 3, totalPages: 3 },
    });
    await flushPromises();

    expect(wrapper.find('[data-testid="device-logs-select-device"]').exists()).toBe(true);
    expect(wrapper.text()).not.toContain('STALE LOG MUST NOT RENDER');
  });

  it('shows 403 details and retries without losing context', async () => {
    getByLevel.mockRejectedValueOnce({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['当前账号无权读取该设备日志'],
    });
    const { wrapper } = await mountLogs({ processId: processAId, deviceId: deviceAId });
    await flushPromises();
    await wrapper.findAll('button').find((item) => item.text() === '查询')!.trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('当前账号无权读取该设备日志');

    await wrapper.findAll('button').find((item) => item.text() === '重试')!.trigger('click');
    await flushPromises();
    expect(getByLevel).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain('当前设备暂无日志数据');
  });

  it('keeps authorization-load failure separate and retryable', async () => {
    getScopedDevices.mockRejectedValueOnce({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['设备授权范围已失效'],
    });
    const { wrapper } = await mountLogs();
    await flushPromises();
    expect(wrapper.find('[data-testid="device-logs-error"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('设备授权范围已失效');
    expect(getByLevel).not.toHaveBeenCalled();

    await wrapper.findAll('button').find((item) => item.text() === '重试')!.trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="device-logs-select-process"]').exists()).toBe(true);
  });
});
