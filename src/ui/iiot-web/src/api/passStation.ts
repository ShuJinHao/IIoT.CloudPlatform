import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

export interface InjectionPassListItemDto {
  id: string;
  deviceId: string;
  barcode: string;
  cellResult: string;
  preInjectionTime: string;
  preInjectionWeight: number;
  postInjectionTime: string;
  postInjectionWeight: number;
  injectionVolume: number;
  completedTime: string;
  receivedAt: string;
}

export interface InjectionPassDetailDto {
  id: string;
  deviceId: string;
  cellResult: string;
  completedTime: string;
  receivedAt: string;
  barcode: string;
  preInjectionTime: string;
  preInjectionWeight: number;
  postInjectionTime: string;
  postInjectionWeight: number;
  injectionVolume: number;
}

const basePath = '/human/pass-stations/injection';

export const getInjectionByBarcodeAndProcessApi = (params: {
  pagination?: Pagination;
  processId: string;
  barcode: string;
}) => {
  return http.get<PagedList<InjectionPassListItemDto>>(`${basePath}/by-barcode-process`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      processId: params.processId,
      barcode: params.barcode,
    },
  });
};

export const getInjectionByTimeAndProcessApi = (params: {
  pagination?: Pagination;
  processId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<InjectionPassListItemDto>>(`${basePath}/by-time-process`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      processId: params.processId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

export const getInjectionByDeviceAndBarcodeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  barcode: string;
}) => {
  return http.get<PagedList<InjectionPassListItemDto>>(`${basePath}/by-device-barcode`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      barcode: params.barcode,
    },
  });
};

export const getInjectionByDeviceAndTimeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<InjectionPassListItemDto>>(`${basePath}/by-device-time`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

export const getInjectionLatest200ByDeviceApi = (params: {
  pagination?: Pagination;
  deviceId: string;
}) => {
  return http.get<PagedList<InjectionPassListItemDto>>(`${basePath}/device/${params.deviceId}/latest`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
    },
  });
};

export const getInjectionDetailApi = (id: string) => {
  return http.get<InjectionPassDetailDto>(`${basePath}/${id}`);
};
