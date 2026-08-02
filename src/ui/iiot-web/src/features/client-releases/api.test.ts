import { beforeEach, describe, expect, it, vi } from 'vitest';
import http from '../../core/http/httpClient';
import {
  getClientReleaseCatalogApi,
  getClientReleaseComponentDeletionsApi,
  getClientReleaseHistoryApi,
  generateEdgeInstallerPackageApi,
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
    hardDeleteClientReleaseComponentApi(
      'component-guid-1',
      '退役旧组件',
      'DELETE AP',
    );
    const [url, config] = httpMock.delete.mock.calls[0]!;
    expect(url).toBe('/human/client-releases/components/component-guid-1');
    expect(config?.data).toEqual({ reason: '退役旧组件', confirmation: 'DELETE AP' });
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

describe('edge installer generation API', () => {
  it('requires and returns the immutable generation id response header', async () => {
    const blob = new Blob(['installer']);
    httpMock.postRaw.mockResolvedValueOnce({
      data: blob,
      headers: {
        'content-disposition': 'attachment; filename="IIoT.EdgeClient-installer.exe"',
        'x-iiot-installer-generation-id': '11111111-1111-1111-1111-111111111111',
      },
    } as never);

    const result = await generateEdgeInstallerPackageApi({
      selections: [{ moduleId: 'CP', deviceId: 'device-1' }],
    });

    expect(result).toEqual({
      blob,
      fileName: 'IIoT.EdgeClient-installer.exe',
      generationId: '11111111-1111-1111-1111-111111111111',
    });
  });

  it('rejects a download response without generationId', async () => {
    httpMock.postRaw.mockResolvedValueOnce({
      data: new Blob(['installer']),
      headers: {},
    } as never);

    await expect(generateEdgeInstallerPackageApi({
      selections: [{ moduleId: 'CP', deviceId: 'device-1' }],
    })).rejects.toThrow('generationId');
  });
});
