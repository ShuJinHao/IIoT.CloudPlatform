import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { deviceRoutes } from './routes';
import { isDeviceDeleteConfirmDisabled } from './types';

describe('devices feature guards', () => {
  it('requires device read permission on the device route', () => {
    expect(deviceRoutes).toHaveLength(1);
    const route = deviceRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('devices');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('requires exact device name before cascade delete confirmation is enabled', () => {
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '')).toBe(true);
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '一号')).toBe(true);
    expect(isDeviceDeleteConfirmDisabled('一号注液机', '一号注液机')).toBe(false);
  });
});
