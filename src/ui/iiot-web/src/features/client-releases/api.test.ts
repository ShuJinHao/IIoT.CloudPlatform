import { beforeEach, describe, expect, it, vi } from 'vitest';
import http from '../../core/http/httpClient';
import {
  getClientReleaseCatalogApi,
  getClientReleaseComponentDeletionsApi,
  getClientReleaseHistoryApi,
  hardDeleteClientReleaseComponentApi,
  retryClientReleaseComponentDeletionApi,
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

describe('client release catalog API', () => {
  it('requests catalog without the removed includeArchived parameter', () => {
    getClientReleaseCatalogApi({ channel: 'stable', targetRuntime: 'win-x64', onlyPublished: true });
    const [url, config] = httpMock.get.mock.calls[0]!;
    expect(url).toBe('/human/client-releases/catalog');
    expect(config?.params).toEqual({
      channel: 'stable',
      targetRuntime: 'win-x64',
      onlyPublished: true,
    });
    expect(config?.params).not.toHaveProperty('includeArchived');
  });

  it('omits empty channel and targetRuntime', () => {
    getClientReleaseCatalogApi({ channel: '', targetRuntime: undefined });
    const [, config] = httpMock.get.mock.calls[0]!;
    expect(config?.params.channel).toBeUndefined();
    expect(config?.params.targetRuntime).toBeUndefined();
  });
});

describe('client release history API', () => {
  it('requests the independent history endpoint with pagination', () => {
    getClientReleaseHistoryApi({ channel: 'stable', pageNumber: 2, pageSize: 10 });
    const [url, config] = httpMock.get.mock.calls[0]!;
    expect(url).toBe('/human/client-releases/history');
    expect(config?.params).toMatchObject({ channel: 'stable', pageNumber: 2, pageSize: 10 });
  });

  it('defaults pageNumber and pageSize', () => {
    getClientReleaseHistoryApi({});
    const [, config] = httpMock.get.mock.calls[0]!;
    expect(config?.params.pageNumber).toBe(1);
    expect(config?.params.pageSize).toBe(10);
  });
});

describe('client release permanent delete API', () => {
  it('deletes by componentId with the reason, not by version id', () => {
    hardDeleteClientReleaseComponentApi('component-guid-1', '退役旧组件');
    const [url, config] = httpMock.delete.mock.calls[0]!;
    expect(url).toBe('/human/client-releases/components/component-guid-1');
    expect(config?.data).toEqual({ reason: '退役旧组件' });
  });

  it('lists pending component deletions', () => {
    getClientReleaseComponentDeletionsApi();
    expect(httpMock.get.mock.calls[0]![0]).toBe('/human/client-releases/component-deletions');
  });

  it('retries a deletion by deletionId', () => {
    retryClientReleaseComponentDeletionApi('deletion-guid-9');
    expect(httpMock.post.mock.calls[0]![0]).toBe(
      '/human/client-releases/component-deletions/deletion-guid-9/retry',
    );
  });
});
