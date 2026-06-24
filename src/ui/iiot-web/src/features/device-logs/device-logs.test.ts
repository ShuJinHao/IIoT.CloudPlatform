import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { deviceLogRoutes } from './routes';
import {
  createDeviceLogFilters,
  emptyDeviceLogMetaData,
  levelToSeverity,
  validateDeviceLogSearch,
} from './types';

describe('device logs feature', () => {
  it('guards the route by device read permission', () => {
    expect(deviceLogRoutes[0]!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('starts searches from empty criteria and pagination', () => {
    const filters = createDeviceLogFilters();
    expect(filters.level).toBeNull();
    expect(filters.keyword).toBe('');
    expect(emptyDeviceLogMetaData()).toMatchObject({
      totalCount: 0,
      pageSize: 20,
      currentPage: 1,
      totalPages: 1,
    });
  });

  it('maps log levels to severity badges', () => {
    expect(levelToSeverity('INFO')).toBe('info');
    expect(levelToSeverity('WARN')).toBe('warn');
    expect(levelToSeverity('ERROR')).toBe('error');
  });

  it('validates mode-specific search inputs before API calls', () => {
    const filters = createDeviceLogFilters();
    expect(validateDeviceLogSearch('keyword', filters)).toBe('请输入关键字。');
    filters.keyword = 'alarm';
    expect(validateDeviceLogSearch('keyword', filters)).toBeNull();
    filters.date = '';
    expect(validateDeviceLogSearch('date-keyword', filters)).toBe('请选择日期并输入关键字。');
  });
});
