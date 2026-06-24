import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { roleRoutes } from './routes';
import {
  rolePermissionSummary,
  togglePermission,
  validateRoleName,
} from './types';

describe('roles feature', () => {
  it('keeps roles management behind role definition permission', () => {
    expect(roleRoutes[0]!.meta?.requiredPermission).toBe(Permissions.Role.Define);
  });

  it('summarizes permission loading status', () => {
    expect(rolePermissionSummary('Operator', { Operator: ['Device.Read'] }, { Operator: 'loaded' })).toBe('1 项');
    expect(rolePermissionSummary('Operator', {}, { Operator: 'failed' })).toBe('加载失败');
    expect(rolePermissionSummary('Operator', {}, { Operator: 'loading' })).toBe('加载中');
  });

  it('toggles permissions without duplicates', () => {
    const permissions = ['Device.Read'];
    togglePermission(permissions, 'Device.Read', true);
    togglePermission(permissions, 'Recipe.Read', true);
    togglePermission(permissions, 'Device.Read', false);
    expect(permissions).toEqual(['Recipe.Read']);
  });

  it('requires role name before creating role policy', () => {
    expect(validateRoleName('')).toBe('角色名称不能为空');
    expect(validateRoleName('Operator')).toBeNull();
  });
});
