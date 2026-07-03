import http from '../../core/http/httpClient';
import type { PagedList, Pagination } from '../../core/types/pagination';

const basePath = '/human/edge-hosts';

export interface EdgeHostListItemDto {
  id: string;
  deviceId: string;
  clientCode: string;
  hostName: string;
  enabled: boolean;
  plcBindingCount: number;
  enabledPlcBindingCount: number;
  remark?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface EdgeHostDto {
  id: string;
  deviceId: string;
  clientCode: string;
  hostName: string;
  enabled: boolean;
  remark?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  plcBindings: EdgeHostPlcBindingDto[];
}

export interface EdgeHostPlcBindingDto {
  id: string;
  plcCode: string;
  plcName: string;
  processId?: string | null;
  businessDeviceId?: string | null;
  stationCode?: string | null;
  protocol?: string | null;
  address?: string | null;
  enabled: boolean;
  displayOrder: number;
  remark?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface EdgeHostPlcRuntimeStateDto {
  id: string;
  edgeHostId: string;
  deviceId: string;
  clientCode: string;
  plcBindingId?: string | null;
  plcCode: string;
  reportedPlcName?: string | null;
  isConfigured: boolean;
  configEnabled?: boolean | null;
  processId?: string | null;
  businessDeviceId?: string | null;
  configuredStationCode?: string | null;
  configuredProtocol?: string | null;
  configuredAddress?: string | null;
  runtimeStationCode?: string | null;
  runtimeProtocol?: string | null;
  runtimeAddress?: string | null;
  isConnected: boolean;
  runtimeStatus: string;
  lastError?: string | null;
  lastSeenAtUtc: string;
  updatedAtUtc: string;
}

export interface DailySummaryDto {
  totalCount: number;
  okCount: number;
  ngCount: number;
  dayShiftTotal: number;
  dayShiftOk: number;
  dayShiftNg: number;
  nightShiftTotal: number;
  nightShiftOk: number;
  nightShiftNg: number;
}

export interface EdgeHostPlcCapacitySummaryDto {
  plcBindingId: string;
  plcCode: string;
  plcName: string;
  bindingEnabled: boolean;
  processId?: string | null;
  businessDeviceId?: string | null;
  date: string;
  canReadCapacity: boolean;
  capacityStatus: string;
  summary?: DailySummaryDto | null;
}

export interface CreateEdgeHostPayload {
  deviceId: string;
  clientCode: string;
  hostName: string;
  remark?: string | null;
}

export interface UpdateEdgeHostPayload {
  edgeHostId: string;
  hostName: string;
  remark?: string | null;
}

export interface AddEdgeHostPlcBindingPayload {
  edgeHostId: string;
  plcCode: string;
  plcName: string;
  processId?: string | null;
  businessDeviceId?: string | null;
  stationCode?: string | null;
  protocol?: string | null;
  address?: string | null;
  displayOrder: number;
  remark?: string | null;
  enabled: boolean;
}

export interface UpdateEdgeHostPlcBindingPayload {
  edgeHostId: string;
  bindingId: string;
  plcName: string;
  processId?: string | null;
  businessDeviceId?: string | null;
  stationCode?: string | null;
  protocol?: string | null;
  address?: string | null;
  displayOrder: number;
  remark?: string | null;
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

export const getEdgeHostDetailApi = (id: string) =>
  http.get<EdgeHostDto>(`${basePath}/${id}`);

export const getEdgeHostPlcRuntimeStatesApi = (id: string) =>
  http.get<EdgeHostPlcRuntimeStateDto[]>(`${basePath}/${id}/plc-runtime-states`);

export const getEdgeHostPlcCapacitySummaryApi = (id: string, date: string) =>
  http.get<EdgeHostPlcCapacitySummaryDto[]>(`${basePath}/${id}/plc-capacity-summary`, {
    params: { date },
  });

export const createEdgeHostApi = (payload: CreateEdgeHostPayload) =>
  http.post<EdgeHostDto>(basePath, payload);

export const updateEdgeHostApi = (id: string, payload: UpdateEdgeHostPayload) =>
  http.put<EdgeHostDto>(`${basePath}/${id}`, payload);

export const enableEdgeHostApi = (id: string) =>
  http.post<EdgeHostDto>(`${basePath}/${id}/enable`);

export const disableEdgeHostApi = (id: string) =>
  http.post<EdgeHostDto>(`${basePath}/${id}/disable`);

export const deleteEdgeHostApi = (id: string) =>
  http.delete<void>(`${basePath}/${id}`);

export const addEdgeHostPlcBindingApi = (
  edgeHostId: string,
  payload: AddEdgeHostPlcBindingPayload,
) =>
  http.post<EdgeHostDto>(`${basePath}/${edgeHostId}/plc-bindings`, payload);

export const updateEdgeHostPlcBindingApi = (
  edgeHostId: string,
  bindingId: string,
  payload: UpdateEdgeHostPlcBindingPayload,
) =>
  http.put<EdgeHostDto>(`${basePath}/${edgeHostId}/plc-bindings/${bindingId}`, payload);

export const enableEdgeHostPlcBindingApi = (edgeHostId: string, bindingId: string) =>
  http.post<EdgeHostDto>(`${basePath}/${edgeHostId}/plc-bindings/${bindingId}/enable`);

export const disableEdgeHostPlcBindingApi = (edgeHostId: string, bindingId: string) =>
  http.post<EdgeHostDto>(`${basePath}/${edgeHostId}/plc-bindings/${bindingId}/disable`);

export const removeEdgeHostPlcBindingApi = (edgeHostId: string, bindingId: string) =>
  http.delete<EdgeHostDto>(`${basePath}/${edgeHostId}/plc-bindings/${bindingId}`);
