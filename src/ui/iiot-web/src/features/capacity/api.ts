import http from '../../core/http/httpClient';
import type { PagedList } from '../../core/types/pagination';

export interface DailyCapacityItem {
  deviceId: string;
  deviceName: string;
  date: string;
  totalCount: number;
  okCount: number;
  ngCount: number;
  okRate: number;
  reportedAt: string;
}

export interface HourlyCapacityItem {
  hour: number;
  minute: number;
  timeLabel: string;
  shiftCode: string;
  totalCount: number;
  okCount: number;
  ngCount: number;
  plcName?: string | null;
}

export interface HourlyCapacityAggregateItem {
  hour: number;
  minute: number;
  timeLabel: string;
  totalCount: number;
  okCount: number;
  ngCount: number;
}

export interface DailySummaryItem {
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

export interface DailyRangeSummaryDto {
  date: string;
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

const basePath = '/human/capacity';

export const getDailyPagedApi = (params: {
  PageNumber?: number;
  PageSize?: number;
  date?: string;
  deviceId?: string;
}) =>
  http.get<PagedList<DailyCapacityItem>>(`${basePath}/daily`, {
    params: {
      PageNumber: params.PageNumber ?? 1,
      PageSize: params.PageSize ?? 10,
      date: params.date || undefined,
      deviceId: params.deviceId || undefined,
    },
  });

export const getHourlyByDeviceApi = (params: {
  deviceId: string;
  date: string;
  plcName?: string;
}) =>
  http.get<HourlyCapacityItem[]>(`${basePath}/hourly`, {
    params: {
      deviceId: params.deviceId,
      date: params.date,
      plcName: params.plcName || undefined,
    },
  });

export const getHourlyAggregateApi = (params: {
  date: string;
  processId?: string;
}) =>
  http.get<HourlyCapacityAggregateItem[]>(`${basePath}/hourly/aggregate`, {
    params: {
      date: params.date,
      processId: params.processId || undefined,
    },
  });

export const getDailySummaryApi = (params: {
  deviceId: string;
  date: string;
  plcName?: string;
}) =>
  http.get<DailySummaryItem | null>(`${basePath}/summary`, {
    params: {
      deviceId: params.deviceId,
      date: params.date,
      plcName: params.plcName || undefined,
    },
  });

export const getSummaryRangeApi = (params: {
  deviceId: string;
  startDate: string;
  endDate: string;
  plcName?: string;
}) =>
  http.get<DailyRangeSummaryDto[]>(`${basePath}/summary/range`, {
    params: {
      deviceId: params.deviceId,
      startDate: params.startDate,
      endDate: params.endDate,
      plcName: params.plcName || undefined,
    },
  });
