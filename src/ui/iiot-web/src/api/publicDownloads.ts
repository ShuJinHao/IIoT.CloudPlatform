import http from '../utils/http';

const basePath = '/public/client-downloads';

export interface PublicClientDownloadCatalogDto {
  catalogSchemaVersion: number;
  channel: string;
  targetRuntime: string;
  latestHost?: PublicClientHostDownloadDto | null;
  plugins: PublicClientPluginCatalogItemDto[];
  generatedAtUtc: string;
}

export interface PublicClientHostDownloadDto {
  channel: string;
  version: string;
  hostApiVersion: string;
  targetRuntime: string;
  targetFramework?: string | null;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  publisher?: string | null;
  publishedAtUtc?: string | null;
}

export interface PublicClientPluginCatalogItemDto {
  moduleId: string;
  displayName: string;
  description?: string | null;
  iconKind?: string | null;
  accentColor?: string | null;
  channel: string;
  version: string;
  hostApiVersion: string;
  minHostVersion: string;
  maxHostVersion: string;
  targetRuntime: string;
  targetFramework?: string | null;
  packageSize: number;
  releaseNotes?: string | null;
  dependencies: unknown[];
  publisher?: string | null;
  publishedAtUtc?: string | null;
}

export const getPublicClientDownloadsApi = (params: {
  channel?: string;
  targetRuntime?: string;
}) => {
  return http.get<PublicClientDownloadCatalogDto>(`${basePath}/latest`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
    },
  });
};
