import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, reactive } from 'vue';
import { Permissions } from '../../types/permissions';
import type { DeviceDeletionImpactDto, DeviceListItemDto } from './api';
import { createDeviceColumns } from './columns';
import { deviceRoutes } from './routes';
import { isDeviceDeleteConfirmDisabled } from './types';
import { useDevices } from './useDevices';

const deviceApiMocks = vi.hoisted(() => ({
  getDevicePagedListApi: vi.fn(),
  getDeviceDeletionImpactApi: vi.fn(),
  deleteDeviceApi: vi.fn(),
  registerDeviceApi: vi.fn(),
  updateDeviceProfileApi: vi.fn(),
}));

vi.mock('./api', () => deviceApiMocks);

const processApiMocks = vi.hoisted(() => ({
  getAllProcessesApi: vi.fn(),
}));

vi.mock('../processes/api', () => processApiMocks);

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
  totalAssociatedRows: 78,
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

function mountDeviceActions(canDeleteDevice: () => boolean) {
  const actionColumn = createDeviceColumns({
    canUpdateDevice: () => false,
    canDeleteDevice,
    processLabel: () => '注液',
    onDetail: vi.fn(),
    onEdit: vi.fn(),
    onDelete: vi.fn(),
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
    deviceApiMocks.deleteDeviceApi.mockResolvedValue(true);
    processApiMocks.getAllProcessesApi.mockResolvedValue([]);
  });

  it('requires device read permission on the device route', () => {
    expect(deviceRoutes).toHaveLength(1);
    const route = deviceRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('devices');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('does not require retyping device name before cascade delete confirmation', () => {
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '')).toBe(false);
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '一号')).toBe(false);
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '一号注液机')).toBe(false);
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

    expect(state.confirmDialog.show).toBe(false);
    expect(deviceApiMocks.getDeviceDeletionImpactApi).not.toHaveBeenCalled();
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
  });

  it('keeps the full cascade-confirmation flow for an Admin with no raw permissions', async () => {
    authMock.state!.isAdmin = true;
    authMock.state!.permissions = [];
    const state = useDevices();
    const actions = mountDeviceActions(() => state.canDeleteDevice.value);

    expect(state.canDeleteDevice.value).toBe(true);
    expect(actions.text()).toContain('删除');
    expect(authMock.hasAllPermissions).toHaveBeenCalledWith([
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ]);

    await state.handleDelete(device);

    expect(deviceApiMocks.getDeviceDeletionImpactApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.getDeviceDeletionImpactApi).toHaveBeenCalledWith(device.id);
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
    expect(state.confirmDialog.show).toBe(true);
    expect(state.confirmDialog.title).toBe('确认级联删除设备');
    expect(state.confirmDialog.impact).toEqual(deletionImpact);
    expect(state.deletionImpactRows.value).toEqual([
      { label: '配方', value: 1 },
      { label: '产能记录', value: 2 },
      { label: '设备日志', value: 3 },
      { label: '过站数据', value: 4 },
      { label: '客户端状态投影', value: 5 },
      { label: '客户端版本快照', value: 6 },
      { label: '插件版本快照', value: 7 },
      { label: '运行心跳', value: 8 },
      { label: '上传幂等登记', value: 9 },
      { label: '人员设备授权', value: 10 },
      { label: '设备 refresh token', value: 11 },
      { label: 'PLC 运行状态', value: 12 },
    ]);
    expect(state.deletionImpactRows.value).toHaveLength(12);
    expect(
      state.deletionImpactRows.value.reduce((total, item) => total + item.value, 0),
    ).toBe(deletionImpact.totalAssociatedRows);

    await state.confirmDialog.onConfirm();

    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledWith(device.id);
    expect(
      deviceApiMocks.getDeviceDeletionImpactApi.mock.invocationCallOrder[0]!,
    ).toBeLessThan(deviceApiMocks.deleteDeviceApi.mock.invocationCallOrder[0]!);
    expect(state.confirmDialog.show).toBe(false);
    expect(state.confirmDialog.impact).toBeNull();
  });

  it('blocks final deletion when the Admin identity is lost after impact preview', async () => {
    authMock.state!.isAdmin = true;
    authMock.state!.permissions = [];
    const state = useDevices();

    await state.handleDelete(device);

    expect(state.confirmDialog.show).toBe(true);
    expect(deviceApiMocks.getDeviceDeletionImpactApi).toHaveBeenCalledTimes(1);

    authMock.state!.isAdmin = false;
    authMock.state!.permissions = [
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ];
    await nextTick();

    expect(state.canDeleteDevice.value).toBe(false);

    await state.confirmDialog.onConfirm();

    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
    expect(state.confirmDialog.show).toBe(true);
    expect(state.confirmDialog.impact).toEqual(deletionImpact);
  });
});
