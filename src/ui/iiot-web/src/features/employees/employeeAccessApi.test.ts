import { beforeEach, describe, expect, it, vi } from 'vitest';
import http from '../../core/http/httpClient';
import { getEmployeeAccessDeviceCandidatesApi } from '../devices/api';

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

describe('employee access device candidate API', () => {
  it('uses the dedicated minimal-candidate route instead of the Admin-only all route', () => {
    getEmployeeAccessDeviceCandidatesApi();

    expect(httpMock.get).toHaveBeenCalledTimes(1);
    expect(httpMock.get).toHaveBeenCalledWith(
      '/human/devices/employee-access-candidates',
    );
    expect(httpMock.get).not.toHaveBeenCalledWith('/human/devices/all');
  });
});
