// src/api/capacity.ts
import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

/** 分页返回包装 */
export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

// ==========================================
// 查询 API（云端后台产能看板）
// ==========================================

/** 所有机台产能分页查询 — GET /api/v1/capacity/daily */
export const getDailyCapacityPagedApi = (params: {
  pagination?: Pagination;
  date?: string;
  deviceId?: string;
}) => {
  return http.get<PagedList<any>>('/capacity/daily', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      date: params.date || undefined,
      deviceId: params.deviceId || undefined,
    },
  });
};

/** 单机台产能汇总查询 — GET /api/v1/capacity/summary */
export const getDeviceCapacitySummaryApi = (params: {
  deviceId: string;
  startDate: string;
  endDate: string;
}) => {
  return http.get<any[]>('/capacity/summary', {
    params: {
      deviceId: params.deviceId,
      startDate: params.startDate,
      endDate: params.endDate,
    },
  });
};