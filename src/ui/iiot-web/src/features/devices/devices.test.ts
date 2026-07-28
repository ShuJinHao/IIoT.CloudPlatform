import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
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
  isAdmin: false,
  permissions: [] as string[],
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    get isAdmin() {
      return authMock.isAdmin;
    },
    get permissions() {
      return authMock.permissions;
    },
    hasPermission: (permission: string) =>
      authMock.isAdmin || authMock.permissions.includes(permission),
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
  totalAssociatedRows: 66,
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
    authMock.isAdmin = false;
    authMock.permissions = [];
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
    authMock.permissions = [
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

  it.each([
    ['Device.Delete', [Permissions.Device.Delete]],
    ['Device.CascadeDelete', [Permissions.Device.CascadeDelete]],
  ])('blocks an Admin who only has %s', async (_label, permissions) => {
    authMock.isAdmin = true;
    authMock.permissions = permissions;
    const state = useDevices();
    const actions = mountDeviceActions(() => state.canDeleteDevice.value);

    expect(state.canDeleteDevice.value).toBe(false);
    expect(actions.text()).not.toContain('删除');

    await state.handleDelete(device);

    expect(state.confirmDialog.show).toBe(false);
    expect(deviceApiMocks.getDeviceDeletionImpactApi).not.toHaveBeenCalled();
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
  });

  it('keeps the full cascade-confirmation flow for an Admin with both permissions', async () => {
    authMock.isAdmin = true;
    authMock.permissions = [
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ];
    const state = useDevices();
    const actions = mountDeviceActions(() => state.canDeleteDevice.value);

    expect(state.canDeleteDevice.value).toBe(true);
    expect(actions.text()).toContain('删除');

    await state.handleDelete(device);

    expect(deviceApiMocks.getDeviceDeletionImpactApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.getDeviceDeletionImpactApi).toHaveBeenCalledWith(device.id);
    expect(deviceApiMocks.deleteDeviceApi).not.toHaveBeenCalled();
    expect(state.confirmDialog.show).toBe(true);
    expect(state.confirmDialog.title).toBe('确认级联删除设备');
    expect(state.confirmDialog.impact).toEqual(deletionImpact);
    expect(state.deletionImpactRows.value).toHaveLength(11);

    await state.confirmDialog.onConfirm();

    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledTimes(1);
    expect(deviceApiMocks.deleteDeviceApi).toHaveBeenCalledWith(device.id);
    expect(state.confirmDialog.show).toBe(false);
    expect(state.confirmDialog.impact).toBeNull();
  });
});
