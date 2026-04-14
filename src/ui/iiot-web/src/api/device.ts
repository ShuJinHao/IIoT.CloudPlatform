import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface DeviceListItemDto {
  id: string;
  deviceName: string;
  processId: string;
}

export interface DeviceSelectDto {
  id: string;
  deviceName: string;
  processId: string;
}

export interface RegisterDevicePayload {
  deviceName: string;
  macAddress: string;
  clientCode: string;
  processId: string;
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

export const registerDeviceApi = (payload: RegisterDevicePayload) => {
  return http.post<string>(basePath, payload);
};

export const updateDeviceProfileApi = (id: string, payload: UpdateDeviceProfilePayload) => {
  return http.put<boolean>(`${basePath}/${id}`, payload);
};

export const deleteDeviceApi = (id: string) => {
  return http.delete<boolean>(`${basePath}/${id}`);
};
