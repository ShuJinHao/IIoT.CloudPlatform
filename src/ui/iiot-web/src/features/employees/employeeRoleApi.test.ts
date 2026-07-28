import { beforeEach, describe, expect, it, vi } from 'vitest';
import http from '../../core/http/httpClient';
import { updateEmployeeRoleApi } from './api';

vi.mock('../../core/http/httpClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    postRaw: vi.fn(),
  },
}));

const httpMock = vi.mocked(http);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('employee role API', () => {
  it('uses the dedicated route and sends only the canonical role payload', () => {
    updateEmployeeRoleApi('employee-1', { roleName: 'HrAdmin' });

    expect(httpMock.put).toHaveBeenCalledWith(
      '/human/employees/employee-1/role',
      { roleName: 'HrAdmin' },
    );
    expect(Object.keys(httpMock.put.mock.calls[0]![1] as object)).toEqual(['roleName']);
    expect(httpMock.put.mock.calls[0]![1]).not.toHaveProperty('employeeId');
  });

  it('preserves explicit null as the clear-role command', () => {
    updateEmployeeRoleApi('employee-2', { roleName: null });

    expect(httpMock.put).toHaveBeenCalledWith(
      '/human/employees/employee-2/role',
      { roleName: null },
    );
    expect(httpMock.put.mock.calls[0]![1]).toHaveProperty('roleName', null);
    expect(httpMock.put.mock.calls[0]![1]).not.toHaveProperty('employeeId');
  });
});
