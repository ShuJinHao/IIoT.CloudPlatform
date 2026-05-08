import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

export interface DeviceLogListItemDto {
  id: string;
  deviceId: string;
  deviceName: string;
  level: string;
  message: string;
  logTime: string;
  receivedAt: string;
}

export interface RecentAlertCountDto {
  count: number;
  sinceHours: number;
  minLevel: string;
  windowStart: string;
  windowEnd: string;
  generatedAt: string;
}

const basePath = '/human/device-logs';

export const getRecentDeviceLogsApi = (params?: {
  limit?: number;
  minLevel?: string;
  processId?: string;
}) => {
  return http.get<DeviceLogListItemDto[]>(`${basePath}/recent`, {
    params: {
      limit: params?.limit ?? 20,
      minLevel: params?.minLevel || 'WARN',
      processId: params?.processId || undefined,
    },
  });
};

export const getRecentAlertCountApi = (params?: {
  sinceHours?: number;
  minLevel?: string;
  processId?: string;
}) => {
  return http.get<RecentAlertCountDto>(`${basePath}/recent-alerts/count`, {
    params: {
      sinceHours: params?.sinceHours ?? 24,
      minLevel: params?.minLevel || 'WARN',
      processId: params?.processId || undefined,
    },
  });
};

export const getLogsByDeviceAndLevelApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  level?: string;
}) => {
  return http.get<PagedList<DeviceLogListItemDto>>(`${basePath}/by-level`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      level: params.level || undefined,
    },
  });
};

export const getLogsByDeviceAndKeywordApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  keyword: string;
}) => {
  return http.get<PagedList<DeviceLogListItemDto>>(`${basePath}/by-keyword`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      keyword: params.keyword,
    },
  });
};

export const getLogsByDeviceAndDateApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  date: string;
}) => {
  return http.get<PagedList<DeviceLogListItemDto>>(`${basePath}/by-date`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      date: params.date,
    },
  });
};

export const getLogsByDeviceAndTimeRangeApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  startTime: string;
  endTime: string;
}) => {
  return http.get<PagedList<DeviceLogListItemDto>>(`${basePath}/by-time-range`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      startTime: params.startTime,
      endTime: params.endTime,
    },
  });
};

export const getLogsByDeviceDateAndKeywordApi = (params: {
  pagination?: Pagination;
  deviceId: string;
  date: string;
  keyword: string;
}) => {
  return http.get<PagedList<DeviceLogListItemDto>>(`${basePath}/by-date-keyword`, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      deviceId: params.deviceId,
      date: params.date,
      keyword: params.keyword,
    },
  });
};
