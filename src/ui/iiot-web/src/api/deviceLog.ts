// src/api/deviceLog.ts
import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

/** 分页返回包装 */
export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

// ==========================================
// 查询 API（云端后台设备日志）
// ==========================================

/** 日志查询一：设备 + 级别 — GET /api/v1/devicelog/by-level */
export const getLogsByDeviceAndLevelApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  level?: string;
}) => {
  return http.get<PagedList<any>>('/devicelog/by-level', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      level: params.level || undefined,
    },
  });
};

/** 日志查询二：设备 + 关键字 — GET /api/v1/devicelog/by-keyword */
export const getLogsByDeviceAndKeywordApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  keyword: string;
}) => {
  return http.get<PagedList<any>>('/devicelog/by-keyword', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      keyword: params.keyword,
    },
  });
};

/** 日志查询三：设备 + 日期 — GET /api/v1/devicelog/by-date */
export const getLogsByDeviceAndDateApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  date: string;
}) => {
  return http.get<PagedList<any>>('/devicelog/by-date', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      date: params.date,
    },
  });
};

/** 日志查询四：设备 + 时间范围 — GET /api/v1/devicelog/by-time-range */
export const getLogsByDeviceAndTimeRangeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<any>>('/devicelog/by-time-range', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

/** 日志查询五：设备 + 日期 + 关键字 — GET /api/v1/devicelog/by-date-keyword */
export const getLogsByDeviceDateAndKeywordApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  date: string;
  keyword: string;
}) => {
  return http.get<PagedList<any>>('/devicelog/by-date-keyword', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      date: params.date,
      keyword: params.keyword,
    },
  });
};