import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { employeeRoutes } from './routes';
import { isResetPasswordInvalid } from './types';

describe('employees feature guards', () => {
  it('requires employee read permission on the route', () => {
    const route = employeeRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('employees');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Employee.Read);
  });

  it('validates reset password confirmation', () => {
    expect(isResetPasswordInvalid('', '')).toBe('请输入新密码');
    expect(isResetPasswordInvalid('Password1', 'Password2')).toBe('两次输入的密码不一致');
    expect(isResetPasswordInvalid('Password1', 'Password1')).toBeNull();
  });
});
