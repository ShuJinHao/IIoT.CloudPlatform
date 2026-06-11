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

// 首装绑定包：一行 = 一个插件 + 一台设备，云端据此生成 moduleId -> 唯一码 清单
export interface EdgeBindingSelection {
  moduleId: string;
  deviceId: string;
}

export interface EdgeBindingItemDto {
  moduleId: string;
  clientCode: string;
  bootstrapSecret: string;
  deviceName: string;
  processId: string;
}

export interface EdgeBindingBundleDto {
  schemaVersion: number;
  baseUrl?: string | null;
  generatedAtUtc: string;
  bindings: EdgeBindingItemDto[];
}

export const generateEdgeBindingBundleApi = (payload: {
  selections: EdgeBindingSelection[];
  baseUrl?: string | null;
}) => {
  return http.post<EdgeBindingBundleDto>(`${basePath}/binding-bundle`, payload);
};

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
