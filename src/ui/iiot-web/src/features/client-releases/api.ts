import http from '../../core/http/httpClient';

const basePath = '/human/client-releases';

export interface ClientReleaseCatalogDto {
  catalogSchemaVersion: number;
  channel: string;
  targetRuntime?: string | null;
  host: ClientHostReleaseComponentDto;
  plugins: ClientPluginReleaseComponentDto[];
  generatedAtUtc: string;
}

export interface ClientHostReleaseComponentDto {
  componentKind: 'Host';
  displayName: string;
  versions: ClientHostVersionEntryDto[];
}

export interface ClientPluginReleaseComponentDto {
  componentKind: 'Plugin';
  moduleId: string;
  displayName: string;
  description?: string | null;
  iconKind?: string | null;
  accentColor?: string | null;
  versions: ClientPluginVersionEntryDto[];
}

export interface ClientHostVersionEntryDto {
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
  deletedAtUtc?: string | null;
  deletionReason?: string | null;
  deletionFailure?: string | null;
}

export interface ClientPluginVersionEntryDto {
  id: string;
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
  deletedAtUtc?: string | null;
  deletionReason?: string | null;
  deletionFailure?: string | null;
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
  primaryIp?: string | null;
  localIpAddresses: string[];
  remoteIpAddress?: string | null;
  channel?: string | null;
  hostVersion?: string | null;
  hostApiVersion?: string | null;
  hostUpdateStatus: string;
  hostCompatibilityIssue?: string | null;
  installStatus: string;
  softwareStatus: string;
  currentVersion: string;
  issue?: string | null;
  versionIssue?: string | null;
  cloudIssue?: string | null;
  lastRuntimeHeartbeatAtUtc?: string | null;
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
  compatibilityIssue?: string | null;
}

export interface ClientReleaseRetentionPolicyDto {
  maxVersionsPerComponent: number;
  updatedAtUtc: string;
}

export interface ClientReleaseFileDeletionResultDto {
  releaseId: string;
  componentKind: string;
  componentName: string;
  channel: string;
  version: string;
  filesDeleted: boolean;
  deletedPaths: string[];
  skippedPaths: string[];
  warning?: string | null;
}

export const getClientReleaseCatalogApi = (params: {
  channel?: string;
  targetRuntime?: string;
  onlyPublished?: boolean;
  includeArchived?: boolean;
}) =>
  http.get<ClientReleaseCatalogDto>(`${basePath}/catalog`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
      onlyPublished: params.onlyPublished ?? false,
      includeArchived: params.includeArchived ?? false,
    },
  });

export const getClientReleaseRetentionPolicyApi = () =>
  http.get<ClientReleaseRetentionPolicyDto>(`${basePath}/retention-policy`);

export const updateClientReleaseRetentionPolicyApi = (payload: {
  maxVersionsPerComponent: number;
}) =>
  http.put<ClientReleaseRetentionPolicyDto>(`${basePath}/retention-policy`, payload);

export const archiveClientReleaseApi = (releaseId: string) =>
  http.delete<void>(`${basePath}/${releaseId}`);

export const deleteClientReleaseFilesApi = (releaseId: string, reason?: string | null) =>
  http.delete<ClientReleaseFileDeletionResultDto>(`${basePath}/${releaseId}/package`, {
    data: { reason: reason ?? null },
  });

export const updateClientReleaseStatusApi = (releaseId: string, status: string) =>
  http.put<void>(`${basePath}/${releaseId}/status`, { status });

export const getDeviceClientVersionInventoryApi = (params: {
  channel?: string;
  targetRuntime?: string;
  keyword?: string;
}) =>
  http.get<DeviceClientVersionInventoryDto[]>(`${basePath}/device-inventory`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
      keyword: params.keyword || undefined,
    },
  });

export const upsertClientHostReleaseApi = (payload: UpsertClientHostReleasePayload) =>
  http.post<UpsertClientReleaseResultDto>(`${basePath}/host-releases`, payload);

export const upsertClientPluginReleaseApi = (payload: UpsertClientPluginReleasePayload) =>
  http.post<UpsertClientReleaseResultDto>(`${basePath}/plugin-releases`, payload);

export interface EdgeBindingSelection {
  moduleId: string;
  deviceId: string;
}

export interface GenerateEdgeInstallerPackagePayload {
  channel?: string | null;
  targetRuntime?: string | null;
  hostVersion?: string | null;
  baseUrl?: string | null;
  selections: EdgeBindingSelection[];
}

export interface EdgeInstallerPackageDownload {
  blob: Blob;
  fileName: string;
}

const parseDownloadFileName = (contentDisposition?: string): string | null => {
  if (!contentDisposition) return null;

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (encoded?.[1]) {
    try {
      return decodeURIComponent(encoded[1].replace(/"/g, '').trim());
    } catch {
      return encoded[1].replace(/"/g, '').trim();
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plain?.[1]?.trim() || null;
};

export const generateEdgeInstallerPackageApi = async (
  payload: GenerateEdgeInstallerPackagePayload,
): Promise<EdgeInstallerPackageDownload> => {
  const response = await http.postRaw<Blob>(`${basePath}/installer-package`, payload, {
    responseType: 'blob',
    timeout: 120000,
  });
  const contentDisposition = response.headers['content-disposition'] as string | undefined;
  return {
    blob: response.data,
    fileName: parseDownloadFileName(contentDisposition) || 'IIoT.EdgeClient-installer.exe',
  };
};
