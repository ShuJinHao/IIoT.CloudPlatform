// src/api/passStation.ts
import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

/** 分页返回包装 */
export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

// ==========================================
// 查询 API（云端后台追溯）
// ==========================================

/** 追溯查询一：条码 + 工序 — GET /api/v1/passstation/injection/by-barcode-process */
export const getInjectionByBarcodeAndProcessApi = (params: {
  pagination?: Pagination;
  processId: string;
  barcode: string;
}) => {
  return http.get<PagedList<any>>('/passstation/injection/by-barcode-process', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      processId: params.processId,
      barcode: params.barcode,
    },
  });
};

/** 追溯查询二：时间范围 + 工序 — GET /api/v1/passstation/injection/by-time-process */
export const getInjectionByTimeAndProcessApi = (params: {
  pagination?: Pagination;
  processId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<any>>('/passstation/injection/by-time-process', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      processId: params.processId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

/** 追溯查询三：设备 + 条码 — GET /api/v1/passstation/injection/by-device-barcode */
export const getInjectionByDeviceAndBarcodeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  barcode: string;
}) => {
  return http.get<PagedList<any>>('/passstation/injection/by-device-barcode', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      barcode: params.barcode,
    },
  });
};

/** 追溯查询四：设备 + 时间范围 — GET /api/v1/passstation/injection/by-device-time */
export const getInjectionByDeviceAndTimeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<any>>('/passstation/injection/by-device-time', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

/** 详情查询 — GET /api/v1/passstation/injection/{id} */
export const getInjectionDetailApi = (id: string) => {
  return http.get<any>(`/passstation/injection/${id}`);
};