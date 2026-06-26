import http from '../core/http/httpClient';

const basePath = '/public/client-downloads';

export interface PublicClientDownloadCatalogDto {
  catalogSchemaVersion: number;
  channel: string;
  targetRuntime: string;
  host: PublicClientHostDownloadComponentDto;
  plugins: PublicClientPluginCatalogComponentDto[];
  generatedAtUtc: string;
}

export interface PublicClientHostDownloadComponentDto {
  componentKind: 'Host';
  displayName: string;
  versions: PublicClientHostVersionDto[];
}

export interface PublicClientHostVersionDto {
  channel: string;
  version: string;
  hostApiVersion: string;
  targetRuntime: string;
  targetFramework?: string | null;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  status: string;
  publisher?: string | null;
  publishedAtUtc?: string | null;
}

export interface PublicClientPluginCatalogComponentDto {
  componentKind: 'Plugin';
  moduleId: string;
  displayName: string;
  description?: string | null;
  iconKind?: string | null;
  accentColor?: string | null;
  versions: PublicClientPluginVersionDto[];
}

export interface PublicClientPluginVersionDto {
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
  status: string;
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
