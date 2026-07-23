import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import { i18n } from '../../i18n';
import { Permissions } from '../../types/permissions';
import DeviceClientOverviewPage from './DeviceClientOverviewPage.vue';
import { deviceClientOverviewRoutes } from './routes';
import { releaseStatusText, softwareStatusText } from './columns';
import { useDeviceClientOverviews } from './useDeviceClientOverviews';

// ===== API 模块整体打桩：组合式函数走真实流程，只有边界是假的 =====
const apiMocks = vi.hoisted(() => ({
  getDeviceClientOverviewsApi: vi.fn(),
  getEdgeHostPlcRuntimeStatesApi: vi.fn(),
  getDeviceClientReleaseDetailsApi: vi.fn(),
}));

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>();
  return { ...actual, ...apiMocks };
});

const authMock = vi.hoisted(() => ({
  plc: true,
  release: true,
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    hasPermission: (permission: string) => {
      if (permission === Permissions.EdgeHost.Read) return authMock.plc;
      if (permission === Permissions.ClientRelease.Read) return authMock.release;
      return false;
    },
  }),
}));

const DEVICE_ID = '1c2b3a4d-5555-4666-8777-888899990000';
const SECOND_DEVICE_ID = '9d8c7b6a-5555-4666-8777-888899990000';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function makeOverviewItem(overrides: Record<string, unknown> = {}) {
  return {
    deviceId: DEVICE_ID,
    deviceName: '一号模切机',
    primaryIpAddress: '10.0.0.11',
    softwareStatus: 'Running',
    currentVersion: '2.4.0',
    issue: null,
    ...overrides,
  };
}

function makeOverviewPage(items: unknown[], totalCount = items.length) {
  return {
    items,
    metaData: { currentPage: 1, totalPages: Math.max(1, Math.ceil(totalCount / 10)), pageSize: 10, totalCount },
  };
}

function makeReleaseDetails(overrides: Record<string, unknown> = {}) {
  return {
    deviceId: DEVICE_ID,
    deviceName: '一号模切机',
    clientCode: 'DC-0001',
    primaryIp: '10.0.0.11',
    localIpAddresses: ['10.0.0.11'],
    remoteIpAddress: null,
    channel: 'stable',
    hostVersion: '2.4.0',
    hostApiVersion: '1.0.0',
    hostUpdateStatus: 'Latest',
    hostCompatibilityIssue: null,
    installStatus: 'Normal',
    softwareStatus: 'Running',
    currentVersion: '2.4.0',
    issue: null,
    versionIssue: null,
    cloudIssue: null,
    lastRuntimeHeartbeatAtUtc: '2026-07-23T01:00:00Z',
    reportedAtUtc: '2026-07-23T00:30:00Z',
    receivedAtUtc: null,
    plugins: [
      { moduleId: 'die-cutting', displayName: '模切', version: '1.2.0', hostApiVersion: '1.0.0', enabled: true, updateStatus: 'Latest', compatibilityIssue: null },
    ],
    ...overrides,
  };
}

function makePlcState(overrides: Record<string, unknown> = {}) {
  return {
    id: 'plc-state-1',
    deviceId: DEVICE_ID,
    clientCode: 'DC-0001',
    plcCode: 'PLC-01',
    reportedPlcName: '一号 PLC',
    runtimeStationCode: 'ST-01',
    runtimeProtocol: 'S7',
    runtimeAddress: '192.168.1.10',
    isConnected: true,
    runtimeStatus: 'Connected',
    lastError: null,
    lastSeenAtUtc: '2026-07-23T01:00:00Z',
    updatedAtUtc: '2026-07-23T01:00:00Z',
    ...overrides,
  };
}

function axiosError(status: number, data: unknown) {
  const err = new axios.AxiosError(`Request failed with status code ${status}`);
  err.response = { status, data, headers: {}, config: {} as never, statusText: String(status) };
  return err;
}

beforeEach(() => {
  vi.clearAllMocks();
  authMock.plc = true;
  authMock.release = true;
  apiMocks.getDeviceClientOverviewsApi.mockResolvedValue(makeOverviewPage([makeOverviewItem()]));
  apiMocks.getEdgeHostPlcRuntimeStatesApi.mockResolvedValue([makePlcState()]);
  apiMocks.getDeviceClientReleaseDetailsApi.mockResolvedValue(makeReleaseDetails());
});

describe('路由与权限常量', () => {
  it('统一主视图路由使用 DeviceClientOverview.Read 门禁', () => {
    expect(deviceClientOverviewRoutes).toHaveLength(1);
    const route = deviceClientOverviewRoutes[0]!;
    expect(route.path).toBe('device-client-overviews');
    expect(route.meta?.requiredPermission).toBe(Permissions.DeviceClientOverview.Read);
    expect(Permissions.DeviceClientOverview.Read).toBe('DeviceClientOverview.Read');
  });
});

describe('状态文案映射', () => {
  it('软件状态映射与后端判定器枚举一致', () => {
    expect(softwareStatusText('Running')).toBe('运行中');
    expect(softwareStatusText('Starting')).toBe('启动中');
    expect(softwareStatusText('Stopped')).toBe('已停止');
    expect(softwareStatusText('RuntimeHeartbeatStale')).toBe('心跳超时');
    expect(softwareStatusText('MissingRuntimeHeartbeat')).toBe('无运行心跳');
    expect(softwareStatusText('Unknown')).toBe('未知');
  });

  it('版本/升级状态映射中文，未识别值原样返回不伪造', () => {
    expect(releaseStatusText('Normal')).toBe('正常');
    expect(releaseStatusText('Latest')).toBe('已最新');
    expect(releaseStatusText('UpdateAvailable')).toBe('可更新');
    expect(releaseStatusText('Incompatible')).toBe('不兼容');
    expect(releaseStatusText('MissingReport')).toBe('未上报');
    expect(releaseStatusText('NoRelease')).toBe('无发布');
    expect(releaseStatusText('Unknown')).toBe('未知');
    expect(releaseStatusText('SomethingNew')).toBe('SomethingNew');
  });
});

describe('useDeviceClientOverviews 主列表', () => {
  it('分页、keyword、排序参数随请求提交给后端，不做前端全量过滤', async () => {
    apiMocks.getDeviceClientOverviewsApi.mockResolvedValue(makeOverviewPage([makeOverviewItem()], 25));
    const state = useDeviceClientOverviews();
    await state.refresh();
    expect(apiMocks.getDeviceClientOverviewsApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ pageNumber: 1, pageSize: 10, sortBy: 'deviceName', sortDirection: 'asc' }),
    );

    state.keyword.value = '模切';
    state.toggleSort('currentVersion');
    await flushPromises();
    expect(apiMocks.getDeviceClientOverviewsApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: '模切', sortBy: 'currentVersion', sortDirection: 'asc', pageNumber: 1 }),
    );

    state.toggleSort('currentVersion');
    await flushPromises();
    expect(apiMocks.getDeviceClientOverviewsApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ sortBy: 'currentVersion', sortDirection: 'desc' }),
    );

    state.gotoPage(2);
    await flushPromises();
    expect(apiMocks.getDeviceClientOverviewsApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ pageNumber: 2 }),
    );
  });

  it('MissingRuntimeHeartbeat 行原样渲染为数据，不伪造在线', async () => {
    apiMocks.getDeviceClientOverviewsApi.mockResolvedValue(makeOverviewPage([
      makeOverviewItem({ softwareStatus: 'MissingRuntimeHeartbeat', currentVersion: null, issue: '客户端尚未上报运行心跳。' }),
    ]));
    const state = useDeviceClientOverviews();
    await state.refresh();
    expect(state.items.value).toHaveLength(1);
    expect(state.items.value[0]!.softwareStatus).toBe('MissingRuntimeHeartbeat');
    expect(state.items.value[0]!.currentVersion).toBeNull();
    expect(state.items.value[0]!.issue).toBe('客户端尚未上报运行心跳。');
  });

  it('loading/empty/error 三态分离', async () => {
    const deferred = <T,>() => {
      let resolve!: (value: T) => void;
      const promise = new Promise<T>((res) => { resolve = res; });
      return { promise, resolve };
    };
    const first = deferred<ReturnType<typeof makeOverviewPage>>();
    apiMocks.getDeviceClientOverviewsApi.mockReturnValue(first.promise);
    const state = useDeviceClientOverviews();
    const refreshing = state.refresh();
    expect(state.loading.value).toBe(true);
    expect(state.error.value).toBeNull();
    first.resolve(makeOverviewPage([], 0));
    await refreshing;
    expect(state.loading.value).toBe(false);
    expect(state.error.value).toBeNull();
    expect(state.isEmpty.value).toBe(true);

    apiMocks.getDeviceClientOverviewsApi.mockRejectedValue(new Error('network down'));
    await state.refresh();
    expect(state.error.value).toBeTruthy();
    expect(state.loading.value).toBe(false);
    expect(state.items.value).toHaveLength(0);
  });
});

describe('详情抽屉权限独立请求', () => {
  it('真实页面按四种权限组合渲染详情入口和区块，零详情权限不出现空入口', async () => {
    const cases: Array<[boolean, boolean, number, number]> = [
      [true, true, 1, 1],
      [true, false, 1, 0],
      [false, true, 0, 1],
      [false, false, 0, 0],
    ];
    for (const [plc, release, plcCalls, releaseCalls] of cases) {
      vi.clearAllMocks();
      authMock.plc = plc;
      authMock.release = release;
      const wrapper = mount(DeviceClientOverviewPage, {
        attachTo: document.body,
        global: { plugins: [i18n] },
      });
      await flushPromises();

      const detailButtons = wrapper.findAll('button').filter((button) => button.text() === '详情');
      expect(detailButtons).toHaveLength(plc || release ? 1 : 0);

      if (detailButtons[0]) {
        await detailButtons[0].trigger('click');
        await flushPromises();
        const bodyText = document.body.textContent ?? '';
        expect(bodyText.includes('PLC 状态')).toBe(plc);
        expect(bodyText.includes('版本、插件和升级详情')).toBe(release);
      } else {
        expect(document.querySelector('.ui-drawer')).toBeNull();
      }

      expect(apiMocks.getEdgeHostPlcRuntimeStatesApi.mock.calls).toHaveLength(plcCalls);
      expect(apiMocks.getDeviceClientReleaseDetailsApi.mock.calls).toHaveLength(releaseCalls);
      wrapper.unmount();
      document.body.innerHTML = '';
    }
  });

  it('打开 A 后关闭并打开 B，A 的迟到响应不得覆盖 B 的数据或错误状态', async () => {
    apiMocks.getDeviceClientOverviewsApi.mockResolvedValue(makeOverviewPage([
      makeOverviewItem({ deviceId: DEVICE_ID, deviceName: '设备 A' }),
      makeOverviewItem({ deviceId: SECOND_DEVICE_ID, deviceName: '设备 B' }),
    ]));
    const plcA = deferred<ReturnType<typeof makePlcState>[]>();
    const plcB = deferred<ReturnType<typeof makePlcState>[]>();
    const releaseA = deferred<ReturnType<typeof makeReleaseDetails>>();
    const releaseB = deferred<ReturnType<typeof makeReleaseDetails>>();
    apiMocks.getEdgeHostPlcRuntimeStatesApi.mockImplementation(
      (deviceId: string) => deviceId === DEVICE_ID ? plcA.promise : plcB.promise,
    );
    apiMocks.getDeviceClientReleaseDetailsApi.mockImplementation(
      (deviceId: string) => deviceId === DEVICE_ID ? releaseA.promise : releaseB.promise,
    );

    const state = useDeviceClientOverviews();
    await state.refresh();
    state.openDetailDrawer(state.items.value[0]!);
    state.closeDetailDrawer();
    state.openDetailDrawer(state.items.value[1]!);

    plcB.resolve([makePlcState({
      id: 'plc-b',
      deviceId: SECOND_DEVICE_ID,
      plcCode: 'PLC-B',
    })]);
    releaseB.resolve(makeReleaseDetails({
      deviceId: SECOND_DEVICE_ID,
      deviceName: '设备 B',
      currentVersion: '3.0.0',
    }));
    await flushPromises();
    expect(state.selectedDevice.value?.deviceId).toBe(SECOND_DEVICE_ID);
    expect(state.plcStates.value[0]?.deviceId).toBe(SECOND_DEVICE_ID);
    expect(state.releaseDetails.value?.deviceId).toBe(SECOND_DEVICE_ID);

    plcA.resolve([makePlcState({ id: 'plc-a', deviceId: DEVICE_ID, plcCode: 'PLC-A' })]);
    releaseA.reject(axiosError(500, {
      title: 'Internal Server Error',
      detail: '设备 A 的迟到错误',
    }));
    await flushPromises();

    expect(state.selectedDevice.value?.deviceId).toBe(SECOND_DEVICE_ID);
    expect(state.plcStates.value[0]?.deviceId).toBe(SECOND_DEVICE_ID);
    expect(state.releaseDetails.value?.deviceId).toBe(SECOND_DEVICE_ID);
    expect(state.plcError.value).toBeNull();
    expect(state.releaseError.value).toBeNull();
    expect(state.plcLoading.value).toBe(false);
    expect(state.releaseLoading.value).toBe(false);
  });

  it('同一设备重试只接受各分支最新请求，旧请求迟到不得回写', async () => {
    const initialPlc = deferred<ReturnType<typeof makePlcState>[]>();
    const retriedPlc = deferred<ReturnType<typeof makePlcState>[]>();
    const initialRelease = deferred<ReturnType<typeof makeReleaseDetails>>();
    const retriedRelease = deferred<ReturnType<typeof makeReleaseDetails>>();
    apiMocks.getEdgeHostPlcRuntimeStatesApi
      .mockReturnValueOnce(initialPlc.promise)
      .mockReturnValueOnce(retriedPlc.promise);
    apiMocks.getDeviceClientReleaseDetailsApi
      .mockReturnValueOnce(initialRelease.promise)
      .mockReturnValueOnce(retriedRelease.promise);

    const state = useDeviceClientOverviews();
    await state.refresh();
    state.openDetailDrawer(state.items.value[0]!);
    state.retryPlcStates();
    state.retryReleaseDetails();

    retriedPlc.resolve([makePlcState({ id: 'plc-retry', plcCode: 'PLC-RETRY' })]);
    retriedRelease.resolve(makeReleaseDetails({ currentVersion: '3.1.0' }));
    await flushPromises();
    expect(state.plcStates.value[0]?.plcCode).toBe('PLC-RETRY');
    expect(state.releaseDetails.value?.currentVersion).toBe('3.1.0');

    initialPlc.resolve([makePlcState({ id: 'plc-old', plcCode: 'PLC-OLD' })]);
    initialRelease.resolve(makeReleaseDetails({ currentVersion: '2.4.0' }));
    await flushPromises();
    expect(state.plcStates.value[0]?.plcCode).toBe('PLC-RETRY');
    expect(state.releaseDetails.value?.currentVersion).toBe('3.1.0');
    expect(state.plcLoading.value).toBe(false);
    expect(state.releaseLoading.value).toBe(false);
  });

  it('PLC 详情请求保留专属路由 /human/edge-hosts/{deviceId}/plc-runtime-states', async () => {
    const state = useDeviceClientOverviews();
    await state.refresh();
    state.openDetailDrawer(state.items.value[0]!);
    await flushPromises();
    expect(apiMocks.getEdgeHostPlcRuntimeStatesApi).toHaveBeenCalledWith(DEVICE_ID);
    expect(apiMocks.getDeviceClientReleaseDetailsApi).toHaveBeenCalledWith(DEVICE_ID);
  });

  it('PLC 失败不影响版本详情，版本详情失败不影响 PLC；错误内联展示真实 ProblemDetails', async () => {
    apiMocks.getEdgeHostPlcRuntimeStatesApi.mockRejectedValue(axiosError(500, {
      title: 'Internal Server Error',
      detail: 'PLC 投影读取超时',
    }));
    apiMocks.getDeviceClientReleaseDetailsApi.mockRejectedValue(axiosError(403, {
      title: 'Forbidden',
      detail: '当前账号无权读取该设备发布详情。',
    }));
    const state = useDeviceClientOverviews();
    await state.refresh();
    state.openDetailDrawer(state.items.value[0]!);
    await flushPromises();

    expect(state.plcError.value).toContain('PLC 投影读取超时');
    expect(state.plcStates.value).toHaveLength(0);
    expect(state.releaseError.value).toContain('当前账号无权读取该设备发布详情。');
    expect(state.releaseDetails.value).toBeNull();
  });

  it('PLC 失败时版本详情仍成功渲染；重试只重发失败的那一路', async () => {
    apiMocks.getEdgeHostPlcRuntimeStatesApi.mockRejectedValueOnce(new Error('boom'));
    const state = useDeviceClientOverviews();
    await state.refresh();
    state.openDetailDrawer(state.items.value[0]!);
    await flushPromises();

    expect(state.plcError.value).toBeTruthy();
    expect(state.releaseDetails.value?.plugins).toHaveLength(1);

    apiMocks.getEdgeHostPlcRuntimeStatesApi.mockResolvedValue([makePlcState()]);
    state.retryPlcStates();
    await flushPromises();
    expect(state.plcError.value).toBeNull();
    expect(state.plcStates.value).toHaveLength(1);
    // 版本详情没有因为 PLC 重试而重复请求
    expect(apiMocks.getDeviceClientReleaseDetailsApi).toHaveBeenCalledTimes(1);
  });
});
