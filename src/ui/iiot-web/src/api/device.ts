import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface DeviceListItemDto {
  id: string;
  deviceName: string;
  code: string;
  processId: string;
}

export interface DeviceSelectDto {
  id: string;
  deviceName: string;
  code: string;
  processId: string;
}

export interface DeviceStatusSummaryDto {
  total: number;
  online: number;
  warning: number;
  error: number;
  offline: number;
  generatedAt: string;
}

export interface RegisterDevicePayload {
  deviceName: string;
  processId: string;
}

export interface CreateDeviceResultDto {
  id: string;
  code: string;
  bootstrapSecret: string;
}

export interface RotateDeviceBootstrapSecretResultDto {
  id: string;
  code: string;
  bootstrapSecret: string;
}

export interface UpdateDeviceProfilePayload {
  deviceName: string;
}

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

const basePath = '/human/devices';

export const getDevicePagedListApi = (params: {
  PaginationParams?: Pagination;
  Keyword?: string;
}) => {
  return http.get<PagedList<DeviceListItemDto>>(basePath, {
    params: {
      'PaginationParams.PageNumber': params.PaginationParams?.PageNumber ?? 1,
      'PaginationParams.PageSize': params.PaginationParams?.PageSize ?? 10,
      Keyword: params.Keyword || undefined,
    },
  });
};

export const getAllActiveDevicesApi = () => {
  return http.get<DeviceSelectDto[]>(`${basePath}/all`);
};

export const getScopedDeviceSelectApi = () => {
  return http.get<DeviceSelectDto[]>(`${basePath}/select`);
};

export const getDeviceStatusSummaryApi = () => {
  return http.get<DeviceStatusSummaryDto>(`${basePath}/status-summary`);
};

export const registerDeviceApi = (payload: RegisterDevicePayload) => {
  return http.post<CreateDeviceResultDto>(basePath, payload);
};

export const updateDeviceProfileApi = (id: string, payload: UpdateDeviceProfilePayload) => {
  return http.put<boolean>(`${basePath}/${id}`, payload);
};

export const rotateDeviceBootstrapSecretApi = (id: string) => {
  return http.post<RotateDeviceBootstrapSecretResultDto>(`${basePath}/${id}/bootstrap-secret/rotate`);
};

export const deleteDeviceApi = (id: string) => {
  return http.delete<boolean>(`${basePath}/${id}`);
};
