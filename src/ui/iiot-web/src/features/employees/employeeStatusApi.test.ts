import { beforeEach, describe, expect, it, vi } from 'vitest';
import http from '../../core/http/httpClient';
import {
  activateEmployeeApi,
  deactivateEmployeeApi,
} from './api';

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

describe('employee status API', () => {
  it('uses the dedicated activate and deactivate PUT routes', () => {
    activateEmployeeApi('employee-1');
    deactivateEmployeeApi('employee-2');

    expect(httpMock.put).toHaveBeenNthCalledWith(
      1,
      '/human/employees/employee-1/activate',
    );
    expect(httpMock.put).toHaveBeenNthCalledWith(
      2,
      '/human/employees/employee-2/deactivate',
    );
  });
});
