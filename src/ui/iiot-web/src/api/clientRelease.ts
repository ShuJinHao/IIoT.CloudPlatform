import http from '../utils/http';

const basePath = '/human/client-releases';

export interface ClientReleaseCatalogDto {
  catalogSchemaVersion: number;
  channel: string;
  targetRuntime?: string | null;
  latestHost?: ClientHostReleaseDto | null;
  hostReleases: ClientHostReleaseDto[];
  pluginReleases: ClientPluginReleaseDto[];
  generatedAtUtc: string;
}

export interface ClientHostReleaseDto {
  id: string;
  channel: string;
  version: string;
  hostApiVersion: string;
  targetRuntime: string;
  targetFramework?: string | null;
  downloadUrl: string;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  status: string;
  signature?: string | null;
  publisher?: string | null;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
}

export interface ClientPluginReleaseDto {
  id: string;
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
  downloadUrl: string;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  dependencies: unknown[];
  status: string;
  signature?: string | null;
  publisher?: string | null;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
}

export interface UpsertClientHostReleasePayload {
  channel: string;
  version: string;
  hostApiVersion: string;
  targetRuntime: string;
  targetFramework?: string | null;
  downloadUrl: string;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  status: string;
  signature?: string | null;
  publisher?: string | null;
}

export interface UpsertClientPluginReleasePayload {
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
  downloadUrl: string;
  sha256: string;
  packageSize: number;
  releaseNotes?: string | null;
  dependenciesJson?: string | null;
  status: string;
  signature?: string | null;
  publisher?: string | null;
}

export interface UpsertClientReleaseResultDto {
  id: string;
}

export interface DeviceClientVersionInventoryDto {
  deviceId: string;
  deviceName: string;
  clientCode: string;
  channel?: string | null;
  hostVersion?: string | null;
  hostApiVersion?: string | null;
  hostUpdateStatus: string;
  latestHostVersion?: string | null;
  hostCompatibilityIssue?: string | null;
  reportedAtUtc?: string | null;
  receivedAtUtc?: string | null;
  plugins: DeviceClientPluginInventoryDto[];
}

export interface DeviceClientPluginInventoryDto {
  moduleId: string;
  displayName?: string | null;
  version?: string | null;
  hostApiVersion?: string | null;
  enabled: boolean;
  updateStatus: string;
  latestVersion?: string | null;
  compatibilityIssue?: string | null;
}

export const getClientReleaseCatalogApi = (params: {
  channel?: string;
  targetRuntime?: string;
  onlyPublished?: boolean;
}) => {
  return http.get<ClientReleaseCatalogDto>(`${basePath}/catalog`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
      onlyPublished: params.onlyPublished ?? false,
    },
  });
};

export const getDeviceClientVersionInventoryApi = (params: {
  channel?: string;
  targetRuntime?: string;
  keyword?: string;
}) => {
  return http.get<DeviceClientVersionInventoryDto[]>(`${basePath}/device-inventory`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
      keyword: params.keyword || undefined,
    },
  });
};

export const upsertClientHostReleaseApi = (payload: UpsertClientHostReleasePayload) => {
  return http.post<UpsertClientReleaseResultDto>(`${basePath}/host-releases`, payload);
};

export const upsertClientPluginReleaseApi = (payload: UpsertClientPluginReleasePayload) => {
  return http.post<UpsertClientReleaseResultDto>(`${basePath}/plugin-releases`, payload);
};
