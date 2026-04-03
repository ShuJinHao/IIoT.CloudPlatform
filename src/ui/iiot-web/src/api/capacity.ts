import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

// 日汇总记录（总览表格用）
export interface DailyCapacityItem {
  device_id:   string;
  device_name: string;
  date:        string;
  total_count: number;
  ok_count:    number;
  ng_count:    number;
  ok_rate:     number;
  reported_at: string;
}

// 半小时明细记录（日视图用）
export interface HourlyCapacityItem {
  hour:       number;
  minute:     number;
  timeLabel:  string;
  shiftCode:  string;
  totalCount: number;
  okCount:    number;
  ngCount:    number;
}

// 日汇总对象（月/年聚合基础）
export interface DailySummaryItem {
  totalCount:       number;
  okCount:          number;
  ngCount:          number;
  dayShiftTotal:    number;
  dayShiftOk:       number;
  dayShiftNg:       number;
  nightShiftTotal:  number;
  nightShiftOk:     number;
  nightShiftNg:     number;
}

// 区间汇总记录（月/年视图用）
export interface DailyRangeSummaryDto {
  date:              string;
  totalCount:        number;
  okCount:           number;
  ngCount:           number;
  dayShiftTotal:     number;
  dayShiftOk:        number;
  dayShiftNg:        number;
  nightShiftTotal:   number;
  nightShiftOk:      number;
  nightShiftNg:      number;
}

// ── 1. 总览分页（所有设备某日汇总）────────────────────────────────
// GET /api/v1/Capacity/daily
export const getDailyPagedApi = (params: {
  PageNumber?: number;
  PageSize?:   number;
  date?:       string;
  deviceId?:   string;
}) => {
  return http.get<PagedList<DailyCapacityItem>>('/capacity/daily', {
    params: {
      PageNumber: params.PageNumber ?? 1,
      PageSize:   params.PageSize   ?? 10,
      date:       params.date       || undefined,
      deviceId:   params.deviceId   || undefined,
    },
  });
};

// ── 2. 半小时明细（日视图优先调用）────────────────────────────────
// GET /api/v1/Capacity/hourly?deviceId=&date=
export const getHourlyByDeviceApi = (params: {
  deviceId: string;
  date:     string;
}) => {
  return http.get<HourlyCapacityItem[]>('/capacity/hourly', {
    params: {
      deviceId: params.deviceId,
      date:     params.date,
    },
  });
};

// ── 3. 单日汇总（日视图兜底）─────────────────────────────────────
// GET /api/v1/Capacity/summary?deviceId=&date=
export const getDailySummaryApi = (params: {
  deviceId: string;
  date:     string;
}) => {
  return http.get<DailySummaryItem | null>('/capacity/summary', {
    params: {
      deviceId: params.deviceId,
      date:     params.date,
    },
  });
};

// ── 4. 区间汇总（月/年视图，单次请求）─────────────────────────────
// GET /api/v1/Capacity/summary/range?deviceId=&startDate=&endDate=
export const getSummaryRangeApi = (params: {
  deviceId:  string;
  startDate: string;
  endDate:   string;
}) => {
  return http.get<DailyRangeSummaryDto[]>('/capacity/summary/range', {
    params: {
      deviceId:  params.deviceId,
      startDate: params.startDate,
      endDate:   params.endDate,
    },
  });
};