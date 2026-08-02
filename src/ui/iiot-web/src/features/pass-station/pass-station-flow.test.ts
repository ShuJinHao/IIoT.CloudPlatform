import { defineComponent, h } from 'vue';
import { flushPromises, mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter, type LocationQueryRaw } from 'vue-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { i18n } from '../../i18n';
import { getScopedDeviceSelectApi } from '../devices/api';
import {
  exportPassStationsApi,
  getPassStationListApi,
  getPassStationTypesApi,
  type PassStationListItemDto,
  type PassStationTypeDefinitionDto,
} from './api';
import { usePassStation } from './usePassStation';

vi.mock('../devices/api', () => ({ getScopedDeviceSelectApi: vi.fn() }));
vi.mock('./api', () => ({
  exportPassStationsApi: vi.fn(),
  getPassStationDetailApi: vi.fn(),
  getPassStationListApi: vi.fn(),
  getPassStationTypesApi: vi.fn(),
}));
vi.mock('../../utils/feedback', () => ({
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
}));

const getScopedDevices = vi.mocked(getScopedDeviceSelectApi);
const getTypes = vi.mocked(getPassStationTypesApi);
const getList = vi.mocked(getPassStationListApi);
const exportCsv = vi.mocked(exportPassStationsApi);

const cpProcessId = '11111111-1111-1111-1111-111111111111';
const apProcessId = '22222222-2222-2222-2222-222222222222';
const cpDeviceId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const apDeviceId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const definitions: PassStationTypeDefinitionDto[] = ['cp', 'ap'].map((typeKey) => ({
  typeKey,
  displayName: typeKey === 'cp' ? '正极模切' : '负极模切',
  description: `${typeKey} 追溯`,
  supportedModes: [
    'barcode-process',
    'time-process',
    'device-barcode',
    'device-time',
    'device-latest',
  ],
  fields: [
    { key: 'plcCode', label: 'PLC 编码', type: 'string', required: true },
  ],
  listColumns: ['plcCode', 'barcode'],
  detailSections: [{ title: '基础信息', fields: ['plcCode', 'barcode'] }],
}));

function emptyPage(): Awaited<ReturnType<typeof getPassStationListApi>> {
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

async function createState(query: LocationQueryRaw = {}) {
  let state!: ReturnType<typeof usePassStation>;
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  });
  await router.push({ path: '/', query });
  await router.isReady();
  const Harness = defineComponent({
    setup() {
      state = usePassStation();
      return () => h('div');
    },
  });
  const wrapper = mount(Harness, { global: { plugins: [i18n, router] } });
  await state.initialize();
  await flushPromises();
  return { state, router, wrapper };
}

describe('pass station authorized context flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    i18n.global.locale.value = 'zh-CN';
    getScopedDevices.mockResolvedValue([
      {
        id: cpDeviceId,
        deviceName: '正极模切客户端',
        code: 'CP-01',
        processId: cpProcessId,
        processCode: 'CP',
        processName: '正极模切',
      },
      {
        id: apDeviceId,
        deviceName: '负极模切客户端',
        code: 'AP-01',
        processId: apProcessId,
        processCode: 'AP',
        processName: '负极模切',
      },
    ]);
    getTypes.mockResolvedValue(definitions);
    getList.mockResolvedValue(emptyPage());
  });

  it('starts without a selection, filters devices by process, and never auto-queries', async () => {
    const { state } = await createState();

    expect(state.selectedProcessId.value).toBeNull();
    expect(state.selectedDeviceId.value).toBeNull();
    expect(state.processOptions.value).toHaveLength(2);
    expect(state.deviceOptions.value).toEqual([]);
    expect(getList).not.toHaveBeenCalled();

    await state.selectProcess(cpProcessId);
    expect(state.deviceOptions.value).toEqual([
      { label: '正极模切客户端 · CP-01', value: cpDeviceId },
    ]);
    expect(state.selectedDeviceId.value).toBeNull();
    expect(getList).not.toHaveBeenCalled();

    await state.selectDevice(cpDeviceId);
    expect(getList).not.toHaveBeenCalled();
    expect(state.activeQueryModes.value.map((mode) => mode.key)).toEqual([
      'device-barcode',
      'device-time',
      'device-latest',
    ]);
  });

  it('restores only a valid URL pair and queries with a required device identity', async () => {
    const { state } = await createState({ processId: cpProcessId, deviceId: cpDeviceId });
    expect(state.context.value).toMatchObject({ processId: cpProcessId, deviceId: cpDeviceId });
    expect(getList).not.toHaveBeenCalled();

    state.switchMode('device-latest');
    await state.doSearch();

    expect(getList).toHaveBeenCalledWith(expect.objectContaining({
      typeKey: 'cp',
      mode: 'device-latest',
      deviceId: cpDeviceId,
    }));
    expect(getList.mock.calls[0]![0].processId).toBeUndefined();
  });

  it('clears a cross-process URL pair without querying', async () => {
    const { state, router } = await createState({
      processId: apProcessId,
      deviceId: cpDeviceId,
    });

    expect(state.context.value).toBeNull();
    expect(router.currentRoute.value.query).toEqual({});
    expect(getList).not.toHaveBeenCalled();
  });

  it('clears results, pagination, details, and ignores a late response after process change', async () => {
    const late = deferred<ReturnType<typeof emptyPage>>();
    getList.mockReturnValueOnce(late.promise);
    const { state } = await createState({ processId: cpProcessId, deviceId: cpDeviceId });
    state.switchMode('device-latest');
    const pendingSearch = state.doSearch();
    await flushPromises();
    state.currentPage.value = 3;
    state.showDetail.value = true;

    await state.selectProcess(apProcessId);
    expect(state.selectedDeviceId.value).toBeNull();
    expect(state.records.value).toEqual([]);
    expect(state.currentPage.value).toBe(1);
    expect(state.showDetail.value).toBe(false);
    expect(state.searched.value).toBe(false);

    const staleRecord: PassStationListItemDto = {
      id: 'stale',
      deviceId: cpDeviceId,
      barcode: 'STALE',
      cellResult: 'OK',
      completedTime: null,
      receivedAt: null,
      fields: { plcCode: 'PLC-OLD' },
    };
    late.resolve({
      items: [staleRecord],
      metaData: { totalCount: 1, pageSize: 10, currentPage: 3, totalPages: 3 },
    });
    await pendingSearch;
    expect(state.records.value).toEqual([]);
    expect(state.currentPage.value).toBe(1);
  });

  it('keeps a 403 query error visible and allows retry', async () => {
    getList.mockRejectedValueOnce({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['当前账号无权查看该设备过站记录'],
    });
    const { state } = await createState({ processId: cpProcessId, deviceId: cpDeviceId });
    state.switchMode('device-latest');

    await state.doSearch();
    expect(state.queryError.value).toBe('当前账号无权查看该设备过站记录');

    await state.doSearch();
    expect(state.queryError.value).toBeNull();
    expect(state.records.value).toEqual([]);
  });

  it('exports with the same selected-device contract', async () => {
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:pass-stations');
    const revokeObjectUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    exportCsv.mockResolvedValue({ blob: new Blob(['csv']), fileName: 'pass-stations-cp.csv' });
    const { state } = await createState({ processId: cpProcessId, deviceId: cpDeviceId });
    state.filters.barcode = 'CP-CLIP-001';

    await state.doExport();

    expect(exportCsv).toHaveBeenCalledWith(expect.objectContaining({
      typeKey: 'cp',
      mode: 'device-barcode',
      deviceId: cpDeviceId,
      barcode: 'CP-CLIP-001',
    }));
    expect(createObjectUrl).toHaveBeenCalledTimes(1);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:pass-stations');
    createObjectUrl.mockRestore();
    revokeObjectUrl.mockRestore();
  });
});
