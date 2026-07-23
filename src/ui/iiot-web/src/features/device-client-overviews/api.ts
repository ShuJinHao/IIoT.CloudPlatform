import http from '../../core/http/httpClient';
import type { PagedList } from '../../core/types/pagination';

// 冻结契约：统一主视图只返回窄字段，PLC/插件/升级详情必须走下方两个专属接口。
export interface DeviceClientOverviewItemDto {
  deviceId: string;
  deviceName: string;
  primaryIpAddress?: string | null;
  softwareStatus: string;
  currentVersion?: string | null;
  issue?: string | null;
}

export type DeviceClientOverviewPageDto = PagedList<DeviceClientOverviewItemDto>;

export type DeviceClientOverviewSortBy =
  | 'deviceName'
  | 'softwareStatus'
  | 'currentVersion'
  | 'lastRuntimeHeartbeatAtUtc';

export type DeviceClientOverviewSortDirection = 'asc' | 'desc';

export const getDeviceClientOverviewsApi = (params: {
  pageNumber?: number;
  pageSize?: number;
  keyword?: string;
  sortBy?: DeviceClientOverviewSortBy;
  sortDirection?: DeviceClientOverviewSortDirection;
}) =>
  http.get<DeviceClientOverviewPageDto>('/human/device-client-overviews', {
    params: {
      pageNumber: params.pageNumber ?? 1,
      pageSize: params.pageSize ?? 10,
      keyword: params.keyword || undefined,
      sortBy: params.sortBy,
      sortDirection: params.sortDirection,
    },
  });

// ===== PLC 状态详情（EdgeHost.Read，专属接口保持保留） =====
// 详情抽屉内联呈现错误，httpClient 不再额外弹全局通知，避免与抽屉内联错误双层叠加。

export interface EdgeHostPlcRuntimeStateDto {
  id: string;
  deviceId: string;
  clientCode: string;
  plcCode: string;
  reportedPlcName?: string | null;
  runtimeStationCode?: string | null;
  runtimeProtocol?: string | null;
  runtimeAddress?: string | null;
  isConnected: boolean;
  runtimeStatus: string;
  lastError?: string | null;
  lastSeenAtUtc: string;
  updatedAtUtc: string;
}

export const getEdgeHostPlcRuntimeStatesApi = (deviceId: string) =>
  http.get<EdgeHostPlcRuntimeStateDto[]>(`/human/edge-hosts/${deviceId}/plc-runtime-states`, {
    inlineFeedback: true,
  });

// ===== 版本、插件和升级详情（ClientRelease.Read），响应沿用既有 inventory DTO =====

export interface DeviceClientPluginInventoryDto {
  moduleId: string;
  displayName?: string | null;
  version?: string | null;
  hostApiVersion?: string | null;
  enabled: boolean;
  updateStatus: string;
  compatibilityIssue?: string | null;
}

export interface DeviceClientReleaseDetailsDto {
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

export const getDeviceClientReleaseDetailsApi = (
  deviceId: string,
  params: { channel?: string; targetRuntime?: string } = {},
) =>
  http.get<DeviceClientReleaseDetailsDto>(`/human/device-client-overviews/${deviceId}/release-details`, {
    params: {
      channel: params.channel || undefined,
      targetRuntime: params.targetRuntime || undefined,
    },
    inlineFeedback: true,
  });
