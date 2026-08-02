import http from '../../core/http/httpClient';
import type { PagedList } from '../../core/types/pagination';

export interface DailyCapacityItem {
  deviceId: string;
  deviceName: string;
  date: string;
  totalCount: number;
  okCount: number | null;
  ngCount: number | null;
  okRate: number | null;
  reportedAt: string;
}

export interface HourlyCapacityItem {
  hour: number;
  minute: number;
  timeLabel: string;
  shiftCode: string;
  totalCount: number;
  okCount: number | null;
  ngCount: number | null;
  plcCode: string;
  plcName?: string | null;
}

export interface HourlyCapacityAggregateItem {
  hour: number;
  minute: number;
  timeLabel: string;
  totalCount: number;
  okCount: number | null;
  ngCount: number | null;
}

export interface DailyRangeSummaryDto {
  date: string;
  totalCount: number;
  okCount: number | null;
  ngCount: number | null;
  dayShiftTotal: number;
  dayShiftOk: number | null;
  dayShiftNg: number | null;
  nightShiftTotal: number;
  nightShiftOk: number | null;
  nightShiftNg: number | null;
  plcCode?: string | null;
  plcName?: string | null;
}

const basePath = '/human/capacity';

export const getDailyPagedApi = (params: {
  PageNumber?: number;
  PageSize?: number;
  date?: string;
  deviceId: string;
}) =>
  http.get<PagedList<DailyCapacityItem>>(`${basePath}/daily`, {
    inlineFeedback: true,
    params: {
      PageNumber: params.PageNumber ?? 1,
      PageSize: params.PageSize ?? 10,
      date: params.date || undefined,
      deviceId: params.deviceId,
    },
  });

export const getHourlyByDeviceApi = (params: {
  deviceId: string;
  date?: string;
  plcCode?: string;
  plcName?: string;
}) =>
  http.get<HourlyCapacityItem[]>(`${basePath}/hourly`, {
    inlineFeedback: true,
    params: {
      deviceId: params.deviceId,
      date: params.date || undefined,
      plcCode: params.plcCode || undefined,
      plcName: params.plcName || undefined,
    },
  });

export const getHourlyAggregateApi = (params: {
  date?: string;
  processId?: string;
}) =>
  http.get<HourlyCapacityAggregateItem[]>(`${basePath}/hourly/aggregate`, {
    inlineFeedback: true,
    params: {
      date: params.date || undefined,
      processId: params.processId || undefined,
    },
  });

export const getSummaryRangeApi = (params: {
  deviceId: string;
  startDate: string;
  endDate: string;
  breakdownByPlc?: boolean;
  plcCode?: string;
  plcName?: string;
}) =>
  http.get<DailyRangeSummaryDto[]>(`${basePath}/summary/range`, {
    inlineFeedback: true,
    params: {
      deviceId: params.deviceId,
      startDate: params.startDate,
      endDate: params.endDate,
      breakdownByPlc: params.breakdownByPlc ?? true,
      plcCode: params.plcCode || undefined,
      plcName: params.plcName || undefined,
    },
  });
