import axios from 'axios';
import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, reactive } from 'vue';
import { Permissions } from '../../types/permissions';
import type {
  DeviceDeletionImpactDto,
  DeviceListItemDto,
  DeviceProcessMigrationImpactDto,
} from './api';
import { createDeviceColumns } from './columns';
import { deviceRoutes } from './routes';
import { useDevices } from './useDevices';

const deviceApiMocks = vi.hoisted(() => ({
  getDevicePagedListApi: vi.fn(),
  getDeviceDeletionImpactApi: vi.fn(),
  getDeviceLedgerProcessOptionsApi: vi.fn(),
  getAvailableDevicePluginSeriesApi: vi.fn(),
  getDeviceProcessMigrationImpactApi: vi.fn(),
  migrateDeviceProcessApi: vi.fn(),
  deleteDeviceApi: vi.fn(),
  registerDeviceApi: vi.fn(),
  updateDeviceProfileApi: vi.fn(),
}));

vi.mock('./api', () => deviceApiMocks);

const feedbackMocks = vi.hoisted(() => ({
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
}));

vi.mock('../../utils/feedback', () => feedbackMocks);

const authMock = vi.hoisted(() => ({
  state: null as { isAdmin: boolean; permissions: string[] } | null,
  hasAllPermissions: vi.fn(),
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    get isAdmin() {
      return authMock.state?.isAdmin ?? false;
    },
    get permissions() {
      return authMock.state?.permissions ?? [];
    },
    hasPermission: (permission: string) =>
      authMock.state?.isAdmin || authMock.state?.permissions.includes(permission),
    hasAllPermissions: (permissions: string[]) =>
      authMock.hasAllPermissions(permissions),
  }),
}));

const device: DeviceListItemDto = {
  id: 'device-1',
  deviceName: '一号注液机',
  code: 'DEVICE-0001',
  processId: 'process-1',
};

const deletionImpact: DeviceDeletionImpactDto = {
  deviceId: device.id,
  deviceName: device.deviceName,
  clientCode: device.code,
  processId: device.processId,
  recipes: 1,
  capacities: 2,
  deviceLogs: 3,
  passStations: 4,
  clientStates: 5,
  clientVersionSnapshots: 6,
  clientPluginVersions: 7,
  runtimeHeartbeats: 8,
  uploadReceiveRegistrations: 9,
  employeeDeviceAccesses: 10,
  refreshTokenSessions: 11,
  edgeHostPlcRuntimeStates: 12,
  installerPendingCredentials: 13,
  devicePluginBindings: 14,
  totalAssociatedRows: 105,
};

const processOptions = [
  { id: 'process-1', processCode: 'CP', processName: '正极模切' },
  { id: 'process-2', processCode: 'AP', processName: '负极模切' },
];

const migrationImpact: DeviceProcessMigrationImpactDto = {
  deviceId: device.id,
  deviceName: device.deviceName,
  clientCode: device.code,
  sourceProcess: processOptions[0]!,
  targetProcess: processOptions[1]!,
  rowVersion: 42,
  relatedCounts: {
    recipes: 0,
    capacities: 0,
    deviceLogs: 1,
    passStations: 0,
    clientStates: 1,
    clientVersionSnapshots: 1,
    clientPluginVersions: 1,
    runtimeHeartbeats: 1,
    uploadReceiveRegistrations: 0,
    employeeDeviceAccesses: 1,
    refreshTokenSessions: 1,
    edgeHostPlcRuntimeStates: 0,
    installerPendingCredentials: 0,
    totalAssociatedRows: 7,
  },
  blockers: [],
  confirmationText: 'MIGRATE DEVICE-0001 TO AP',
  canMigrate: true,
};

function emptyDevicePage() {
  return {
    items: [],
    metaData: {
      totalCount: 0,
      pageSize: 10,
      currentPage: 1,
      totalPages: 1,
    },
  };
}

function devicePage(items: DeviceListItemDto[], totalCount = items.length) {
  return {
    items,
    metaData: {
      totalCount,
      pageSize: 10,
      currentPage: 1,
      totalPages: Math.max(1, Math.ceil(totalCount / 10)),
    },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function axiosError(status: number, detail: string) {
  const error = new axios.AxiosError(`Request failed with status code ${status}`);
  error.response = {
    status,
    data: { title: String(status), detail },
    headers: {},
    config: {} as never,
    statusText: String(status),
  };
  return error;
}

function mountDeviceActions(
  canDeleteDevice: () => boolean,
  isSubmitting: () => boolean = () => false,
) {
  const actionColumn = createDeviceColumns({
    canUpdateDevice: () => false,
    canDeleteDevice,
    canMigrateDevice: () => false,
    isSubmitting,
    processLabel: () => '注液',
    onDetail: vi.fn(),
    onEdit: vi.fn(),
    onDelete: vi.fn(),
    onMigrate: vi.fn(),
  }).find((column) => column.key === 'actions');

  expect(actionColumn?.render, '设备操作列必须提供 render').toBeTypeOf('function');
  const Harness = defineComponent({
    setup() {
      return () => h('div', [actionColumn!.render!(device, 0)]);
    },
  });
  return mount(Harness);
}

describe('devices feature guards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authMock.state = reactive({
      isAdmin: false,
      permissions: [] as string[],
    });
    authMock.hasAllPermissions.mockImplementation(
      (permissions: string[]) =>
        authMock.state!.isAdmin
        || permissions.every((permission) =>
          authMock.state!.permissions.includes(permission)),
    );
    deviceApiMocks.getDevicePagedListApi.mockResolvedValue(emptyDevicePage());
    deviceApiMocks.getDeviceDeletionImpactApi.mockResolvedValue(deletionImpact);
    deviceApiMocks.getAvailableDevicePluginSeriesApi.mockResolvedValue([]);
    deviceApiMocks.deleteDeviceApi.mockResolvedValue(true);
    deviceApiMocks.getDeviceLedgerProcessOptionsApi.mockResolvedValue(processOptions);
    deviceApiMocks.getDeviceProcessMigrationImpactApi.mockResolvedValue(migrationImpact);
    deviceApiMocks.migrateDeviceProcessApi.mockResolvedValue({
      deviceId: device.id,
      sourceProcessId: 'process-1',
      targetProcessId: 'process-2',
      rowVersion: 43,
    });
  });

  it('requires device read permission on the device route', () => {
    expect(deviceRoutes).toHaveLength(1);
    const route = deviceRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('devices');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('删除影响 DTO 包含插件绑定计数', () => {
    expect(deletionImpact.devicePluginBindings).toBe(14);
    expect(deletionImpact.totalAssociatedRows).toBe(105);
  });

  it('loads process options without selecting the first process or querying devices', async () => {
    const state = useDevices();

    await state.initialize();

    expect(state.selectedProcessId.value).toBeNull();
    expect(state.processOptions.value).toHaveLength(2);
    expect(deviceApiMocks.getDeviceLedgerProcessOptionsApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.getDevicePagedListApi).not.toHaveBeenCalled();
  });

  it('queries only the selected process and locks new-device creation to it', async () => {
    authMock.state!.isAdmin = true;
    const state = useDevices();
    await state.initialize();

    await state.selectProcess('process-2');
    await state.openRegisterModal();

    expect(deviceApiMocks.getDevicePagedListApi).toHaveBeenCalledWith({
      PaginationParams: { PageNumber: 1, PageSize: 10 },
      Keyword: undefined,
      ProcessId: 'process-2',
    });
    expect(state.registerForm.processId).toBe('process-2');
    expect(state.showRegisterModal.value).toBe(true);
  });

  it('requires Admin and exact preflight confirmation for process migration', async () => {
    authMock.state!.isAdmin = true;
    const state = useDevices();

    state.openMigrationDialog(device);
    await state.selectMigrationTarget('process-2');

    expect(deviceApiMocks.getDeviceProcessMigrationImpactApi)
      .toHaveBeenCalledWith(device.id, 'process-2');
    expect(state.migrationDialog.impact).toEqual(migrationImpact);

    state.migrationDialog.confirmInput = 'wrong';
    await state.submitMigration();
    expect(deviceApiMocks.migrateDeviceProcessApi).not.toHaveBeenCalled();

    state.migrationDialog.confirmInput = migrationImpact.confirmationText;
    await state.submitMigration();
    expect(deviceApiMocks.migrateDeviceProcessApi).toHaveBeenCalledWith(device.id, {
      expectedSourceProcessId: 'process-1',
      targetProcessId: 'process-2',
      expectedRowVersion: 42,
      confirmationText: migrationImpact.confirmationText,
    });
    expect(state.migrationDialog.show).toBe(false);
  });

  it('hides and blocks deletion for a non-Admin even with both deletion permissions', async () => {
    authMock.state!.permissions = [
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ];
    const state = useDevices();
    const actions = mountDeviceActions(() => state.canDeleteDevice.value);

    expect(state.canDeleteDevice.value).toBe(false);
    expect(actions.text()).not.toContain('删除');

    await state.handleDelete(device);

    expect(deviceApiMocks.getDeviceDeletionImpactApi).not.toHaveBeenCalled();
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
  });

  it('Admin 点击删除直接调用 DELETE，不请求影响预览也不打开确认框', async () => {
    authMock.state!.isAdmin = true;
    authMock.state!.permissions = [];
    deviceApiMocks.getDevicePagedListApi
      .mockResolvedValueOnce(devicePage([device]))
      .mockResolvedValueOnce(emptyDevicePage());
    const state = useDevices();
    const actions = mountDeviceActions(() => state.canDeleteDevice.value);
    await state.initialize();
    await state.selectProcess('process-1');
    state.openDetailPanel(device);

    expect(state.canDeleteDevice.value).toBe(true);
    expect(actions.text()).toContain('删除');
    expect(authMock.hasAllPermissions).toHaveBeenCalledWith([
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ]);

    await state.handleDelete(device);

    expect(deviceApiMocks.getDeviceDeletionImpactApi).not.toHaveBeenCalled();
    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledWith(device.id);
    expect(state.selectedDevice.value).toBeNull();
    expect(state.showDetailPanel.value).toBe(false);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('设备及其可变关联数据已删除。');
  });

  it('重复点击只发送一个删除请求', async () => {
    authMock.state!.isAdmin = true;
    const pendingDelete = deferred<boolean>();
    deviceApiMocks.deleteDeviceApi.mockReturnValue(pendingDelete.promise);
    const state = useDevices();

    const first = state.handleDelete(device);
    const second = state.handleDelete(device);
    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledTimes(1);
    pendingDelete.resolve(true);
    await Promise.all([first, second]);
  });

  it('请求期间删除按钮保持危险色并禁用', () => {
    const actions = mountDeviceActions(() => true, () => true);
    const deleteButton = actions.findAll('button').find(button => button.text() === '删除');
    expect(deleteButton).toBeDefined();
    expect(deleteButton!.attributes('disabled')).toBeDefined();
    expect(deleteButton!.classes()).toContain('bg-[#ffe7e7]');
  });

  it.each([403, 409, 500])('%i 失败保留选中设备和详情，不替换后端错误', async (status) => {
    authMock.state!.isAdmin = true;
    deviceApiMocks.deleteDeviceApi.mockRejectedValue(axiosError(status, '后端稳定原因'));
    const state = useDevices();
    state.openDetailPanel(device);

    await state.handleDelete(device);

    expect(state.selectedDevice.value).toEqual(device);
    expect(state.showDetailPanel.value).toBe(true);
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).not.toHaveBeenCalled();
    expect(deviceApiMocks.getDevicePagedListApi).not.toHaveBeenCalled();
  });

  it('404 保留详情并刷新列表校正已失效行', async () => {
    authMock.state!.isAdmin = true;
    deviceApiMocks.getDevicePagedListApi.mockResolvedValue(devicePage([device]));
    deviceApiMocks.deleteDeviceApi.mockRejectedValue(axiosError(404, '设备已不存在'));
    const state = useDevices();
    await state.initialize();
    await state.selectProcess('process-1');
    state.openDetailPanel(device);
    deviceApiMocks.getDevicePagedListApi.mockClear();

    await state.handleDelete(device);

    expect(state.selectedDevice.value).toEqual(device);
    expect(state.showDetailPanel.value).toBe(true);
    expect(deviceApiMocks.getDevicePagedListApi).toHaveBeenCalledTimes(1);
  });

  it('删除当前页最后一行时回退到上一页', async () => {
    authMock.state!.isAdmin = true;
    let deleted = false;
    deviceApiMocks.deleteDeviceApi.mockImplementation(async () => {
      deleted = true;
      return true;
    });
    deviceApiMocks.getDevicePagedListApi.mockImplementation(({ PaginationParams }: {
      PaginationParams: { PageNumber: number };
    }) => {
      if (PaginationParams.PageNumber === 2) {
        return Promise.resolve(deleted ? devicePage([], 10) : devicePage([device], 11));
      }
      return Promise.resolve(devicePage([device], deleted ? 10 : 11));
    });
    const state = useDevices();
    await state.initialize();
    await state.selectProcess('process-1');
    state.onPageChange(2);
    await flushPromises();

    await state.handleDelete(device);
    await flushPromises();

    expect(state.currentPage.value).toBe(1);
    expect(deviceApiMocks.getDevicePagedListApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ PaginationParams: expect.objectContaining({ PageNumber: 1 }) }),
    );
  });

  it('删除前发出的迟到列表响应不得复活已删设备', async () => {
    authMock.state!.isAdmin = true;
    const stalePage = deferred<ReturnType<typeof devicePage>>();
    deviceApiMocks.getDevicePagedListApi
      .mockResolvedValueOnce(devicePage([device]))
      .mockReturnValueOnce(stalePage.promise)
      .mockResolvedValueOnce(emptyDevicePage());
    const state = useDevices();
    await state.initialize();
    await state.selectProcess('process-1');
    state.openDetailPanel(device);

    const staleRefresh = state.fetchList();
    const deleting = state.handleDelete(device);
    await deleting;
    stalePage.resolve(devicePage([device]));
    await staleRefresh;

    expect(state.devices.value).toHaveLength(0);
    expect(state.selectedDevice.value).toBeNull();
    expect(state.showDetailPanel.value).toBe(false);
  });

  it('设备工序迁移确认流程保持独立', async () => {
    authMock.state!.isAdmin = true;
    const state = useDevices();
    state.openMigrationDialog(device);
    await state.selectMigrationTarget('process-2');
    state.migrationDialog.confirmInput = migrationImpact.confirmationText;

    await state.submitMigration();

    expect(deviceApiMocks.migrateDeviceProcessApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
  });
});
