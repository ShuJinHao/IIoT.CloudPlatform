import http from '../../core/http/httpClient';
import type { PagedList, Pagination } from '../../core/types/pagination';

const basePath = '/human/edge-hosts';

export interface EdgeHostListItemDto {
  id: string;
  deviceId: string;
  clientCode: string;
  hostName: string;
  primaryIpAddress?: string | null;
  localIpAddresses: string[];
  softwareStatus: string;
  currentVersion?: string | null;
  lastRuntimeHeartbeatAtUtc?: string | null;
  plcCount: number;
  connectedPlcCount: number;
  faultedPlcCount: number;
  lastPlcSeenAtUtc?: string | null;
  issue?: string | null;
}

export interface EdgeHostDto extends EdgeHostListItemDto {
  plcStates: EdgeHostPlcRuntimeStateDto[];
}

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

export const getEdgeHostPagedListApi = (params: {
  pagination?: Pagination;
  keyword?: string;
}) =>
  http.get<PagedList<EdgeHostListItemDto>>(basePath, {
    params: {
      'PaginationParams.PageNumber': params.pagination?.PageNumber ?? 1,
      'PaginationParams.PageSize': params.pagination?.PageSize ?? 10,
      Keyword: params.keyword || undefined,
    },
  });

export const getEdgeHostDetailApi = (deviceId: string) =>
  http.get<EdgeHostDto>(`${basePath}/${deviceId}`);

export const getEdgeHostPlcRuntimeStatesApi = (deviceId: string) =>
  http.get<EdgeHostPlcRuntimeStateDto[]>(`${basePath}/${deviceId}/plc-runtime-states`);
